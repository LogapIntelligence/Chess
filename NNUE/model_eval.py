#!/usr/bin/env python3
"""
NNUE Model Evaluation and Export Utility
Evaluates trained models and exports them in various formats
Compatible with PyTorch 2.9.0
"""

import argparse
import os
import struct
import json
from pathlib import Path
from typing import Dict, List, Tuple, Optional
import time

import numpy as np
import torch
import torch.nn as nn
import chess
import chess.engine
from tqdm import tqdm
import matplotlib.pyplot as plt

# Fixed imports - import from the actual modules
from nnue_trainer import NNUE, TrainingConfig, HalfKPFeatures, DatabaseConnection

class ModelEvaluator:
    """Evaluate and analyze NNUE models"""
    
    def __init__(self, model_path: str, config: Optional[TrainingConfig] = None):
        self.model_path = model_path
        self.device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        
        # Load model with map_location for device compatibility
        checkpoint = torch.load(model_path, map_location=self.device, weights_only=False)
        
        if config is None:
            self.config = checkpoint.get('config', TrainingConfig())
        else:
            self.config = config
            
        self.model = NNUE(self.config).to(self.device)
        self.model.load_state_dict(checkpoint['model_state_dict'])
        self.model.eval()
        
        self.feature_encoder = HalfKPFeatures()
        
        print(f"Loaded model from {model_path}")
        print(f"Training epoch: {checkpoint.get('epoch', 'unknown')}")
        print(f"Validation loss: {checkpoint.get('loss', 'unknown')}")
    
    def evaluate_position(self, fen: str) -> float:
        """Evaluate a single position"""
        white_features, black_features = self.feature_encoder.position_to_features(fen)
        
        board = chess.Board(fen)
        if board.turn == chess.WHITE:
            features = white_features
        else:
            features = black_features
        
        with torch.no_grad():
            features_tensor = torch.tensor(features, dtype=torch.float32).unsqueeze(0).to(self.device)
            evaluation = self.model(features_tensor).item()
            
            # Scale back to centipawns
            evaluation *= self.config.eval_scale
            
            # Flip for black to move
            if board.turn == chess.BLACK:
                evaluation = -evaluation
                
        return evaluation
    
    def benchmark_speed(self, num_positions: int = 1000) -> Dict[str, float]:
        """Benchmark model inference speed"""
        print(f"Benchmarking model speed with {num_positions} positions...")
        
        # Generate random positions
        test_positions = []
        for _ in range(num_positions):
            # Create random position
            board = chess.Board()
            for _ in range(np.random.randint(10, 40)):
                moves = list(board.legal_moves)
                if not moves:
                    break
                board.push(np.random.choice(moves))
            test_positions.append(board.fen())
        
        # Warm up GPU if available
        if self.device.type == 'cuda':
            for _ in range(10):
                self.evaluate_position(test_positions[0])
        
        # Benchmark individual evaluations
        start_time = time.time()
        for fen in test_positions:
            self.evaluate_position(fen)
        single_time = time.time() - start_time
        
        # Benchmark batch evaluation
        features_batch = []
        for fen in test_positions:
            white_features, black_features = self.feature_encoder.position_to_features(fen)
            board = chess.Board(fen)
            features = white_features if board.turn == chess.WHITE else black_features
            features_batch.append(features)
        
        features_tensor = torch.tensor(features_batch, dtype=torch.float32).to(self.device)
        
        # Warm up for batch
        if self.device.type == 'cuda':
            with torch.no_grad():
                for _ in range(10):
                    _ = self.model(features_tensor[:1])
        
        start_time = time.time()
        with torch.no_grad():
            _ = self.model(features_tensor)
        if self.device.type == 'cuda':
            torch.cuda.synchronize()  # Ensure GPU operations complete
        batch_time = time.time() - start_time
        
        results = {
            'positions': num_positions,
            'single_eval_time': single_time,
            'single_nps': num_positions / single_time,
            'batch_eval_time': batch_time,
            'batch_nps': num_positions / batch_time,
            'speedup': single_time / batch_time
        }
        
        print(f"Single evaluation: {results['single_nps']:.0f} NPS")
        print(f"Batch evaluation: {results['batch_nps']:.0f} NPS")
        print(f"Batch speedup: {results['speedup']:.1f}x")
        
        return results
    
    def compare_with_engine(self, engine_path: str, positions: List[str], time_per_move: float = 0.1) -> Dict:
        """Compare model evaluations with chess engine"""
        print(f"Comparing with engine: {engine_path}")
        
        try:
            with chess.engine.SimpleEngine.popen_uci(engine_path) as engine:
                model_evals = []
                engine_evals = []
                
                for fen in tqdm(positions, desc="Comparing evaluations"):
                    # Model evaluation
                    model_eval = self.evaluate_position(fen)
                    model_evals.append(model_eval)
                    
                    # Engine evaluation
                    board = chess.Board(fen)
                    info = engine.analyse(board, chess.engine.Limit(time=time_per_move))
                    
                    if info['score'].is_mate():
                        # Handle mate scores
                        mate_moves = info['score'].white().mate()
                        if mate_moves is not None:
                            engine_eval = 10000 if mate_moves > 0 else -10000
                        else:
                            engine_eval = 0
                    else:
                        engine_eval = info['score'].white().score()
                    
                    engine_evals.append(engine_eval)
                
                # Calculate correlation and statistics
                model_evals = np.array(model_evals)
                engine_evals = np.array(engine_evals)
                
                # Remove extreme values for correlation calculation
                mask = (np.abs(model_evals) < 5000) & (np.abs(engine_evals) < 5000)
                filtered_model = model_evals[mask]
                filtered_engine = engine_evals[mask]
                
                if len(filtered_model) > 1:
                    correlation = np.corrcoef(filtered_model, filtered_engine)[0, 1]
                else:
                    correlation = 0.0
                    
                mae = np.mean(np.abs(filtered_model - filtered_engine))
                rmse = np.sqrt(np.mean((filtered_model - filtered_engine) ** 2))
                
                results = {
                    'correlation': correlation,
                    'mae': mae,
                    'rmse': rmse,
                    'model_evals': model_evals.tolist(),
                    'engine_evals': engine_evals.tolist(),
                    'positions_compared': len(positions)
                }
                
                print(f"Correlation: {correlation:.3f}")
                print(f"MAE: {mae:.1f} cp")
                print(f"RMSE: {rmse:.1f} cp")
                
                return results
                
        except Exception as e:
            print(f"Engine comparison failed: {e}")
            return {}
    
    def analyze_feature_importance(self, positions: List[str]) -> Dict:
        """Analyze which features are most important for the model"""
        print("Analyzing feature importance...")
        
        feature_activations = np.zeros(self.config.input_size)
        feature_weights = np.zeros(self.config.input_size)
        
        # Get first layer weights
        first_layer = None
        for name, param in self.model.named_parameters():
            if 'feature_transformer.0.weight' in name:
                first_layer = param.detach().cpu().numpy()
                break
        
        if first_layer is not None:
            feature_weights = np.mean(np.abs(first_layer), axis=0)
        
        # Analyze activation patterns
        for fen in tqdm(positions[:1000], desc="Analyzing features"):  # Limit for speed
            white_features, black_features = self.feature_encoder.position_to_features(fen)
            board = chess.Board(fen)
            features = white_features if board.turn == chess.WHITE else black_features
            feature_activations += features
        
        feature_activations /= len(positions[:1000])
        
        # Find most important features
        importance_scores = feature_weights * feature_activations
        top_features = np.argsort(importance_scores)[-20:][::-1]
        
        results = {
            'feature_weights': feature_weights.tolist(),
            'feature_activations': feature_activations.tolist(),
            'importance_scores': importance_scores.tolist(),
            'top_features': top_features.tolist()
        }
        
        print(f"Analyzed {min(len(positions), 1000)} positions")
        print("Top 5 most important features:")
        for i, feature_idx in enumerate(top_features[:5]):
            print(f"{i+1}. Feature {feature_idx}: {importance_scores[feature_idx]:.4f}")
        
        return results
    
    def plot_evaluation_distribution(self, positions: List[str], output_path: str = "eval_distribution.png"):
        """Plot distribution of model evaluations"""
        evaluations = []
        
        for fen in tqdm(positions, desc="Evaluating positions"):
            eval_score = self.evaluate_position(fen)
            evaluations.append(eval_score)
        
        evaluations = np.array(evaluations)
        
        plt.figure(figsize=(12, 8))
        
        # Histogram
        plt.subplot(2, 2, 1)
        plt.hist(evaluations, bins=50, alpha=0.7, edgecolor='black')
        plt.title('Evaluation Distribution')
        plt.xlabel('Evaluation (centipawns)')
        plt.ylabel('Frequency')
        
        # Box plot
        plt.subplot(2, 2, 2)
        plt.boxplot(evaluations)
        plt.title('Evaluation Box Plot')
        plt.ylabel('Evaluation (centipawns)')
        
        # Clipped histogram for better visibility
        plt.subplot(2, 2, 3)
        clipped_evals = np.clip(evaluations, -1000, 1000)
        plt.hist(clipped_evals, bins=50, alpha=0.7, edgecolor='black')
        plt.title('Evaluation Distribution (clipped ±1000cp)')
        plt.xlabel('Evaluation (centipawns)')
        plt.ylabel('Frequency')
        
        # Statistics
        plt.subplot(2, 2, 4)
        stats_text = f"""
        Statistics:
        Count: {len(evaluations)}
        Mean: {np.mean(evaluations):.1f} cp
        Median: {np.median(evaluations):.1f} cp
        Std: {np.std(evaluations):.1f} cp
        Min: {np.min(evaluations):.1f} cp
        Max: {np.max(evaluations):.1f} cp
        """
        plt.text(0.1, 0.5, stats_text, fontsize=10, verticalalignment='center')
        plt.axis('off')
        
        plt.tight_layout()
        plt.savefig(output_path, dpi=300, bbox_inches='tight')
        plt.close()
        
        print(f"Evaluation distribution plot saved to {output_path}")

class NNUEExporter:
    """Export NNUE models to various formats"""
    
    def __init__(self, model: NNUE, config: TrainingConfig):
        self.model = model
        self.config = config
    
    def export_stockfish_nnue(self, output_path: str):
        """Export in Stockfish NNUE format (simplified)"""
        print(f"Exporting to Stockfish NNUE format: {output_path}")
        
        try:
            with open(output_path, 'wb') as f:
                # Write header
                f.write(b'NNUE')  # Magic
                f.write(struct.pack('<I', 0x7AF32F16))  # Version hash
                f.write(struct.pack('<I', 177))  # Architecture hash
                
                # Write network description
                description = f"NNUE {self.config.input_size}->{self.config.hidden_size}->1"
                description_bytes = description.encode('utf-8')
                f.write(struct.pack('<I', len(description_bytes)))
                f.write(description_bytes)
                
                # Write feature transformer
                self._write_feature_transformer(f)
                
                # Write output layers
                self._write_output_layers(f)
            
            print(f"Successfully exported to {output_path}")
            
        except Exception as e:
            print(f"Export failed: {e}")
    
    def _write_feature_transformer(self, f):
        """Write feature transformer weights"""
        # Get weights from first layer
        for name, param in self.model.named_parameters():
            if 'feature_transformer.0.weight' in name:
                weights = param.detach().cpu().numpy().astype(np.int16)
                f.write(weights.tobytes())
            elif 'feature_transformer.0.bias' in name:
                bias = param.detach().cpu().numpy().astype(np.int32)
                f.write(bias.tobytes())
    
    def _write_output_layers(self, f):
        """Write output layer weights"""
        for name, param in self.model.named_parameters():
            if 'output_layers' in name:
                if 'weight' in name:
                    weights = param.detach().cpu().numpy().astype(np.int8)
                    f.write(weights.tobytes())
                elif 'bias' in name:
                    bias = param.detach().cpu().numpy().astype(np.int32)
                    f.write(bias.tobytes())
    
    def export_onnx(self, output_path: str):
        """Export model to ONNX format"""
        try:
            import torch.onnx
            
            # Create dummy input
            dummy_input = torch.randn(1, self.config.input_size)
            
            # Use dynamic_axes for flexible batch size
            torch.onnx.export(
                self.model,
                dummy_input,
                output_path,
                export_params=True,
                opset_version=14,  # Use newer opset for PyTorch 2.x
                input_names=['input'],
                output_names=['output'],
                dynamic_axes={'input': {0: 'batch_size'}, 'output': {0: 'batch_size'}}
            )
            
            print(f"ONNX model exported to {output_path}")
            
        except ImportError:
            print("ONNX export requires: pip install onnx")
        except Exception as e:
            print(f"ONNX export failed: {e}")
    
    def export_pytorch_mobile(self, output_path: str):
        """Export model for PyTorch Mobile (updated for PyTorch 2.x)"""
        try:
            # Note: _save_for_lite_interpreter is deprecated in PyTorch 2.x
            # Using standard TorchScript instead
            self.model.eval()
            example_input = torch.randn(1, self.config.input_size)
            traced_model = torch.jit.trace(self.model, example_input)
            
            # Standard save for mobile compatibility
            traced_model.save(output_path)
            
            print(f"PyTorch mobile model exported to {output_path}")
            print("Note: Use standard TorchScript loading in mobile apps")
            
        except AttributeError as e:
            if "_save_for_lite_interpreter" in str(e):
                # Fallback for older PyTorch versions
                try:
                    traced_model = torch.jit.trace(self.model, torch.randn(1, self.config.input_size))
                    optimized_model = torch.jit.optimize_for_inference(traced_model)
                    optimized_model._save_for_lite_interpreter(output_path)
                    print(f"PyTorch Mobile model exported to {output_path}")
                except Exception as e2:
                    print(f"PyTorch Mobile export failed: {e2}")
                    print("Try using ONNX export instead for mobile deployment")
            else:
                print(f"PyTorch Mobile export failed: {e}")
        except Exception as e:
            print(f"PyTorch Mobile export failed: {e}")
            print("This is expected with PyTorch 2.x - use ONNX export instead")

def main():
    parser = argparse.ArgumentParser(description="NNUE Model Evaluation and Export")
    parser.add_argument('model_path', help='Path to trained model (.pth file)')
    parser.add_argument('--benchmark', action='store_true', help='Run speed benchmark')
    parser.add_argument('--compare-engine', type=str, help='Path to UCI engine for comparison')
    parser.add_argument('--analyze-features', action='store_true', help='Analyze feature importance')
    parser.add_argument('--plot-distribution', action='store_true', help='Plot evaluation distribution')
    parser.add_argument('--export-nnue', type=str, help='Export to .nnue format')
    parser.add_argument('--export-onnx', type=str, help='Export to ONNX format')
    parser.add_argument('--export-mobile', type=str, help='Export to PyTorch Mobile format')
    parser.add_argument('--num-positions', type=int, default=1000, help='Number of positions for testing')
    parser.add_argument('--connection-string', type=str, help='Database connection string')
    
    args = parser.parse_args()
    
    if not os.path.exists(args.model_path):
        print(f"Error: Model file not found: {args.model_path}")
        return
    
    # Load model
    evaluator = ModelEvaluator(args.model_path)
    
    # Load test positions from database
    test_positions = []
    if args.connection_string:
        try:
            db = DatabaseConnection(args.connection_string)
            config = TrainingConfig()
            config.connection_string = args.connection_string
            config.max_positions = args.num_positions
            positions_data = db.load_training_data(config)
            test_positions = [pos[0] for pos in positions_data[:args.num_positions]]
        except Exception as e:
            print(f"Could not load positions from database: {e}")
    
    # Generate random positions if no database
    if not test_positions:
        print("Generating random test positions...")
        for _ in range(args.num_positions):
            board = chess.Board()
            for _ in range(np.random.randint(10, 40)):
                moves = list(board.legal_moves)
                if not moves:
                    break
                board.push(np.random.choice(moves))
            test_positions.append(board.fen())
    
    print(f"Using {len(test_positions)} test positions")
    
    # Run requested evaluations
    if args.benchmark:
        evaluator.benchmark_speed(args.num_positions)
    
    if args.compare_engine:
        evaluator.compare_with_engine(args.compare_engine, test_positions[:100])
    
    if args.analyze_features:
        evaluator.analyze_feature_importance(test_positions)
    
    if args.plot_distribution:
        evaluator.plot_evaluation_distribution(test_positions)
    
    # Export model
    exporter = NNUEExporter(evaluator.model, evaluator.config)
    
    if args.export_nnue:
        exporter.export_stockfish_nnue(args.export_nnue)
    
    if args.export_onnx:
        exporter.export_onnx(args.export_onnx)
    
    if args.export_mobile:
        exporter.export_pytorch_mobile(args.export_mobile)
    
    print("Evaluation completed!")

if __name__ == "__main__":
    main()