#!/usr/bin/env python3
"""
NNUE - Main module for chess position evaluation training
"""

# Import all the main classes and functions
from .nnue_trainer import (
    NNUE,
    TrainingConfig, 
    HalfKPFeatures,
    DatabaseConnection,
    ChessDataset,
    NNUETrainer
)

from .model_eval import (
    ModelEvaluator,
    NNUEExporter
)

from .training_config import (
    create_training_config,
    get_training_config,
    TRAINING_PRESETS,
    validate_config
)

# Version info
__version__ = "1.0.0"
__author__ = "Chess NNUE Training System"

# Main entry point
def main():
    """Main entry point for NNUE training"""
    import argparse
    import sys
    
    parser = argparse.ArgumentParser(description="NNUE Chess Evaluation Training")
    parser.add_argument('--train', action='store_true', help='Start training')
    parser.add_argument('--eval', type=str, help='Evaluate model from path')
    parser.add_argument('--config', type=str, default='default', help='Training configuration preset')
    parser.add_argument('--test-connection', action='store_true', help='Test database connection')
    
    # Training arguments
    parser.add_argument('--batch-size', type=int, help='Training batch size')
    parser.add_argument('--learning-rate', type=float, help='Learning rate')
    parser.add_argument('--epochs', type=int, help='Number of epochs')
    parser.add_argument('--max-positions', type=int, help='Maximum training positions')
    parser.add_argument('--device', type=str, choices=['cpu', 'cuda'], help='Training device')
    
    # Evaluation arguments  
    parser.add_argument('--benchmark', action='store_true', help='Run speed benchmark')
    parser.add_argument('--compare-engine', type=str, help='Compare with UCI engine')
    parser.add_argument('--export-nnue', type=str, help='Export to .nnue format')
    
    args = parser.parse_args()
    
    if args.test_connection:
        config = get_training_config(args.config)
        db = DatabaseConnection(config.connection_string)
        try:
            positions = db.load_training_data(config)
            print(f"✓ Database connection successful! Found {len(positions)} positions")
            return 0
        except Exception as e:
            print(f"✗ Database connection failed: {e}")
            return 1
    
    elif args.eval:
        # Model evaluation mode
        evaluator = ModelEvaluator(args.eval)
        
        if args.benchmark:
            evaluator.benchmark_speed()
        
        if args.compare_engine:
            # Load some test positions
            config = get_training_config(args.config)
            db = DatabaseConnection(config.connection_string)
            positions = db.load_training_data(config)
            test_positions = [pos[0] for pos in positions[:100]]
            evaluator.compare_with_engine(args.compare_engine, test_positions)
        
        if args.export_nnue:
            exporter = NNUEExporter(evaluator.model, evaluator.config)
            exporter.export_stockfish_nnue(args.export_nnue)
    
    else:
        # Training mode (default)
        config = get_training_config(args.config)
        
        # Override config with command line arguments
        if args.batch_size:
            config.batch_size = args.batch_size
        if args.learning_rate:
            config.learning_rate = args.learning_rate
        if args.epochs:
            config.epochs = args.epochs
        if args.max_positions:
            config.max_positions = args.max_positions
        if args.device:
            config.device = args.device
        
        # Validate configuration
        issues = validate_config(config)
        if issues:
            print("Configuration issues found:")
            for issue in issues:
                print(f"  - {issue}")
            return 1
        
        # Start training
        trainer = NNUETrainer(config)
        trainer.train()
    
    return 0

if __name__ == "__main__":
    sys.exit(main())