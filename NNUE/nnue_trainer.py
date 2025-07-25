#!/usr/bin/env python3
"""
NNUE Training Application for Chess Position Evaluation
Trains on data generated from the Chess Database application
"""

import argparse
import logging
import os
import sys
import time
from collections import defaultdict
from dataclasses import dataclass
from typing import List, Tuple, Optional, Dict, Any
import struct
import hashlib

import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
import pyodbc
import chess
import chess.engine
from tqdm import tqdm

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler('nnue_training.log'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

@dataclass
class TrainingConfig:
    """Configuration for NNUE training"""
    # Database connection
    connection_string = "Server=localhost\\SQLEXPRESS;Database=ChessDatabase;UID=chess;PWD=chess@123;Driver={ODBC Driver 17 for SQL Server};"
    
    # Model architecture
    input_size: int = 768  # HalfKP features (64 squares * 12 piece types / 2)
    hidden_size: int = 256
    output_size: int = 1
    
    # Training parameters
    batch_size: int = 8192
    learning_rate: float = 0.001
    weight_decay: float = 1e-4
    epochs: int = 1
    validation_split: float = 0.1
    
    # Data parameters
    max_positions: int = 1000000
    eval_scale: float = 361.0  # Scale factor for evaluation (361 = pawn value in cp)
    min_ply: int = 16  # Minimum ply for training positions
    max_eval: float = 10.0  # Maximum evaluation magnitude
    
    # Training options
    device: str = 'cuda' if torch.cuda.is_available() else 'cpu'
    num_workers: int = 0
    save_frequency: int = 10  # Save model every N epochs
    
    # Output
    model_name: str = 'chess_nnue'
    output_dir: str = 'models'

class HalfKPFeatures:
    """HalfKP feature encoding for chess positions"""
    
    def __init__(self):
        self.piece_to_index = {
            chess.PAWN: 0, chess.KNIGHT: 1, chess.BISHOP: 2,
            chess.ROOK: 3, chess.QUEEN: 4, chess.KING: 5
        }
    
    def position_to_features(self, fen: str) -> Tuple[np.ndarray, np.ndarray]:
        """Convert FEN position to HalfKP features for both sides"""
        board = chess.Board(fen)
        
        white_features = self._get_halfkp_features(board, chess.WHITE)
        black_features = self._get_halfkp_features(board, chess.BLACK)
        
        return white_features, black_features
    
    def _get_halfkp_features(self, board: chess.Board, color: chess.Color) -> np.ndarray:
        """Get HalfKP features for one side"""
        features = np.zeros(768, dtype=np.float32)
        
        # Find king position
        king_square = board.king(color)
        if king_square is None:
            return features
        
        # Mirror for black
        if color == chess.BLACK:
            king_square = chess.square_mirror(king_square)
        
        # Encode all pieces relative to king
        for square, piece in board.piece_map().items():
            if piece.piece_type == chess.KING:
                continue
                
            # Mirror square for black perspective
            if color == chess.BLACK:
                square = chess.square_mirror(square)
            
            # Calculate feature index
            piece_color = 0 if piece.color == color else 1
            piece_type_idx = self.piece_to_index[piece.piece_type]
            
            # HalfKP encoding: king_square * 10 * 64 + piece_color * 6 * 64 + piece_type * 64 + square
            feature_idx = (
                king_square * 10 * 64 + 
                piece_color * 6 * 64 + 
                piece_type_idx * 64 + 
                square
            )
            
            if 0 <= feature_idx < 768:
                features[feature_idx] = 1.0
        
        return features

class ChessDataset(Dataset):
    """Dataset for chess positions from database"""
    
    def __init__(self, positions: List[Tuple[str, float, str]], config: TrainingConfig):
        self.positions = positions
        self.config = config
        self.feature_encoder = HalfKPFeatures()
        
        logger.info(f"Created dataset with {len(positions)} positions")
    
    def __len__(self):
        return len(self.positions)
    
    def __getitem__(self, idx):
        fen, evaluation, result = self.positions[idx]
        
        # Get features for both sides
        white_features, black_features = self.feature_encoder.position_to_features(fen)
        
        # Determine side to move
        board = chess.Board(fen)
        if board.turn == chess.WHITE:
            features = white_features
        else:
            features = black_features
            evaluation = -evaluation  # Flip evaluation for black
        
        # Normalize evaluation
        evaluation = np.clip(evaluation / self.config.eval_scale, -1.0, 1.0)
        
        # Convert result to outcome
        outcome = 0.5  # Draw
        if result == "1-0":
            outcome = 1.0 if board.turn == chess.WHITE else 0.0
        elif result == "0-1":
            outcome = 0.0 if board.turn == chess.WHITE else 1.0
        
        return (
            torch.tensor(features, dtype=torch.float32),
            torch.tensor([evaluation], dtype=torch.float32),
            torch.tensor([outcome], dtype=torch.float32)
        )

class NNUE(nn.Module):
    """NNUE architecture for chess evaluation"""
    
    def __init__(self, config: TrainingConfig):
        super().__init__()
        self.config = config
        
        # Feature transformer - processes HalfKP features
        self.feature_transformer = nn.Sequential(
            nn.Linear(config.input_size, config.hidden_size),
            nn.ReLU(),
            nn.Linear(config.hidden_size, 32)
        )
        
        # Output layers
        self.output_layers = nn.Sequential(
            nn.ReLU(),
            nn.Linear(32, 32),
            nn.ReLU(),
            nn.Linear(32, 1),
            nn.Tanh()
        )
        
        # Initialize weights
        self._initialize_weights()
    
    def _initialize_weights(self):
        """Initialize network weights"""
        for module in self.modules():
            if isinstance(module, nn.Linear):
                nn.init.kaiming_normal_(module.weight, nonlinearity='relu')
                nn.init.zeros_(module.bias)
    
    def forward(self, x):
        """Forward pass"""
        # Transform features
        features = self.feature_transformer(x)
        
        # Output evaluation
        output = self.output_layers(features)
        return output

class DatabaseConnection:
    """Database connection and data loading"""
    
    def __init__(self, connection_string: str):
        self.connection_string = connection_string
    
    def load_training_data(self, config: TrainingConfig) -> List[Tuple[str, float, str]]:
        """Load training data from database"""
        logger.info("Loading training data from database...")
    
        # Fixed query - removed DISTINCT since we're deduplicating in Python anyway
        query = """
        SELECT 
            cm.Fen,
            cm.Evaluation, 
            cg.Result,
            cm.MoveNumber
        FROM ChessMoves cm
        INNER JOIN ChessGames cg ON cm.ChessGameId = cg.Id
        WHERE cm.MoveNumber >= ?
            AND ABS(cm.Evaluation) <= ?
            AND cg.Result IN ('1-0', '0-1', '1/2-1/2')
            AND cm.Fen IS NOT NULL
        ORDER BY NEWID()  -- Random order
        """
    
        try:
            conn = pyodbc.connect(self.connection_string)
            cursor = conn.cursor()
        
            cursor.execute(query, (config.min_ply, config.max_eval * config.eval_scale))
        
            positions = []
            seen_zobrist = set()
        
            for row in tqdm(cursor.fetchall(), desc="Loading positions"):
                fen, evaluation, result, move_number = row
            
                # Simple deduplication using zobrist-like hash of FEN
                fen_hash = hashlib.md5(fen.encode()).hexdigest()
                if fen_hash in seen_zobrist:
                    continue
            
                seen_zobrist.add(fen_hash)
                positions.append((fen, float(evaluation), result))
            
                if len(positions) >= config.max_positions:
                    break
        
            conn.close()
            logger.info(f"Loaded {len(positions)} unique positions")
            return positions
        
        except Exception as e:
            logger.error(f"Database error: {e}")
            raise

class NNUETrainer:
    """NNUE training coordinator"""
    
    def __init__(self, config: TrainingConfig):
        self.config = config
        self.device = torch.device(config.device)
        
        # Create output directory
        os.makedirs(config.output_dir, exist_ok=True)
        
        logger.info(f"Using device: {self.device}")
        logger.info(f"Training configuration: {config}")
    
    def train(self):
        """Main training loop"""
        # Load data
        db = DatabaseConnection(self.config.connection_string)
        positions = db.load_training_data(self.config)
        
        if not positions:
            logger.error("No training data loaded!")
            return
        
        # Split data
        split_idx = int(len(positions) * (1 - self.config.validation_split))
        train_positions = positions[:split_idx]
        val_positions = positions[split_idx:]
        
        logger.info(f"Training positions: {len(train_positions)}")
        logger.info(f"Validation positions: {len(val_positions)}")
        
        # Create datasets
        train_dataset = ChessDataset(train_positions, self.config)
        val_dataset = ChessDataset(val_positions, self.config)
        
        # Create data loaders with error handling for workers
        try:
            train_loader = DataLoader(
                train_dataset, 
                batch_size=self.config.batch_size,
                shuffle=True,
                num_workers=self.config.num_workers,
                pin_memory=True if self.device.type == 'cuda' else False,
                persistent_workers=True if self.config.num_workers > 0 else False
            )
            
            val_loader = DataLoader(
                val_dataset,
                batch_size=self.config.batch_size,
                shuffle=False,
                num_workers=self.config.num_workers,
                pin_memory=True if self.device.type == 'cuda' else False,
                persistent_workers=True if self.config.num_workers > 0 else False
            )
        except Exception as e:
            logger.warning(f"Failed to create data loaders with workers: {e}")
            logger.info("Falling back to single-threaded data loading")
            
            train_loader = DataLoader(
                train_dataset, 
                batch_size=self.config.batch_size,
                shuffle=True,
                num_workers=0,
                pin_memory=False
            )
            
            val_loader = DataLoader(
                val_dataset,
                batch_size=self.config.batch_size,
                shuffle=False,
                num_workers=0,
                pin_memory=False
            )
        
        # Create model
        model = NNUE(self.config).to(self.device)
        logger.info(f"Model parameters: {sum(p.numel() for p in model.parameters()):,}")
        
        # Optimizer and loss
        optimizer = optim.AdamW(
            model.parameters(), 
            lr=self.config.learning_rate,
            weight_decay=self.config.weight_decay
        )
        
        # Fixed: Remove verbose parameter for PyTorch 2.9.0 compatibility
        scheduler = optim.lr_scheduler.ReduceLROnPlateau(
            optimizer, mode='min', factor=0.5, patience=5
        )
        
        criterion = nn.MSELoss()
        
        # Training loop
        best_val_loss = float('inf')
        last_lr = optimizer.param_groups[0]['lr']
        
        for epoch in range(self.config.epochs):
            # Training
            model.train()
            train_loss = self._train_epoch(model, train_loader, optimizer, criterion)
            
            # Validation
            model.eval()
            val_loss = self._validate(model, val_loader, criterion)
            
            # Learning rate scheduling
            scheduler.step(val_loss)
            
            # Check if learning rate changed (manual verbose replacement)
            current_lr = optimizer.param_groups[0]['lr']
            if current_lr < last_lr:
                logger.info(f"Reducing learning rate from {last_lr:.8f} to {current_lr:.8f}")
                last_lr = current_lr
            
            logger.info(
                f"Epoch {epoch+1}/{self.config.epochs} - "
                f"Train Loss: {train_loss:.6f}, Val Loss: {val_loss:.6f}, "
                f"LR: {current_lr:.8f}"
            )
            
            # Save best model
            if val_loss < best_val_loss:
                best_val_loss = val_loss
                self._save_model(model, optimizer, epoch, val_loss, "best")
            
            # Regular saves
            if (epoch + 1) % self.config.save_frequency == 0:
                self._save_model(model, optimizer, epoch, val_loss, f"epoch_{epoch+1}")
        
        # Export final model
        self._export_nnue(model, "final")
        logger.info("Training completed!")
    
    def _train_epoch(self, model, train_loader, optimizer, criterion):
        """Train one epoch"""
        total_loss = 0
        num_batches = 0
        
        for features, evaluations, outcomes in tqdm(train_loader, desc="Training"):
            features = features.to(self.device)
            evaluations = evaluations.to(self.device)
            
            optimizer.zero_grad()
            
            predictions = model(features)
            loss = criterion(predictions, evaluations)
            
            loss.backward()
            
            # Gradient clipping for stability
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            
            optimizer.step()
            
            total_loss += loss.item()
            num_batches += 1
        
        return total_loss / num_batches
    
    def _validate(self, model, val_loader, criterion):
        """Validate model"""
        total_loss = 0
        num_batches = 0
        
        with torch.no_grad():
            for features, evaluations, outcomes in val_loader:
                features = features.to(self.device)
                evaluations = evaluations.to(self.device)
                
                predictions = model(features)
                loss = criterion(predictions, evaluations)
                
                total_loss += loss.item()
                num_batches += 1
        
        return total_loss / num_batches
    
    def _save_model(self, model, optimizer, epoch, loss, suffix):
        """Save model checkpoint"""
        checkpoint = {
            'epoch': epoch,
            'model_state_dict': model.state_dict(),
            'optimizer_state_dict': optimizer.state_dict(),
            'loss': loss,
            'config': self.config
        }
        
        filename = f"{self.config.model_name}_{suffix}.pth"
        filepath = os.path.join(self.config.output_dir, filename)
        torch.save(checkpoint, filepath)
        logger.info(f"Saved model: {filepath}")
    
    def _export_nnue(self, model, suffix):
        """Export model in NNUE format for chess engines"""
        model.eval()
        
        filename = f"{self.config.model_name}_{suffix}.nnue"
        filepath = os.path.join(self.config.output_dir, filename)
        
        try:
            # Simple binary export (engine-specific format would need adaptation)
            with open(filepath, 'wb') as f:
                # Write magic number and version
                f.write(b'NNUE')
                f.write(struct.pack('<I', 1))  # Version
                
                # Write architecture info
                f.write(struct.pack('<I', self.config.input_size))
                f.write(struct.pack('<I', self.config.hidden_size))
                f.write(struct.pack('<I', self.config.output_size))
                
                # Write weights and biases
                for name, param in model.named_parameters():
                    data = param.detach().cpu().numpy().astype(np.float32)
                    f.write(data.tobytes())
            
            logger.info(f"Exported NNUE model: {filepath}")
            
        except Exception as e:
            logger.error(f"Export failed: {e}")

def create_config_from_args(args) -> TrainingConfig:
    """Create configuration from command line arguments"""
    config = TrainingConfig()
    
    if args.connection_string:
        config.connection_string = args.connection_string
    if args.batch_size:
        config.batch_size = args.batch_size
    if args.learning_rate:
        config.learning_rate = args.learning_rate
    if args.epochs:
        config.epochs = args.epochs
    if args.max_positions:
        config.max_positions = args.max_positions
    if args.model_name:
        config.model_name = args.model_name
    if args.output_dir:
        config.output_dir = args.output_dir
    if args.device:
        config.device = args.device
    
    return config

def main():
    """Main entry point"""
    parser = argparse.ArgumentParser(description="NNUE Training for Chess Evaluation")
    parser.add_argument('--connection-string', type=str, help='Database connection string')
    parser.add_argument('--batch-size', type=int, default=8192, help='Training batch size')
    parser.add_argument('--learning-rate', type=float, default=0.001, help='Learning rate')
    parser.add_argument('--epochs', type=int, default=1, help='Number of epochs')
    parser.add_argument('--max-positions', type=int, default=1000000, help='Maximum training positions')
    parser.add_argument('--model-name', type=str, default='chess_nnue', help='Model name')
    parser.add_argument('--output-dir', type=str, default='models', help='Output directory')
    parser.add_argument('--device', type=str, choices=['cpu', 'cuda'], help='Training device')
    parser.add_argument('--test-connection', action='store_true', help='Test database connection')
    
    args = parser.parse_args()
    
    # Test database connection if requested
    if args.test_connection:
        config = create_config_from_args(args)
        db = DatabaseConnection(config.connection_string)
        try:
            positions = db.load_training_data(config)
            logger.info(f"Connection test successful! Found {len(positions)} positions")
        except Exception as e:
            logger.error(f"Connection test failed: {e}")
        return
    
    # Create configuration and start training
    config = create_config_from_args(args)
    trainer = NNUETrainer(config)
    trainer.train()

if __name__ == "__main__":
    main()