#!/usr/bin/env python3
"""
Quick start training script for NNUE
Perfect for testing the system with smaller datasets
"""

import os
import sys
import logging
from pathlib import Path

# Add current directory to path so we can import NNUE modules
sys.path.insert(0, os.path.dirname(__file__))

try:
    from nnue_trainer import NNUETrainer, TrainingConfig, DatabaseConnection
    from training_config import validate_config
except ImportError as e:
    print(f"Import error: {e}")
    print("Make sure you're running this from the NNUE directory")
    sys.exit(1)

def create_quick_config():
    """Create a configuration optimized for quick testing"""
    config = TrainingConfig()
    
    # Quick test parameters - smaller and faster
    config.max_positions = 100000     # Small dataset for quick testing
    config.epochs = 20                # Few epochs for quick results
    config.batch_size = 4096          # Moderate batch size
    config.learning_rate = 0.001      # Standard learning rate
    config.hidden_size = 256          # Standard hidden size
    config.model_name = "quick_test"  # Different name to avoid conflicts
    config.save_frequency = 5         # Save more frequently
    config.validation_split = 0.15    # Slightly more validation data
    
    # Try to use GPU if available, fall back to CPU
    import torch
    config.device = 'cuda' if torch.cuda.is_available() else 'cpu'
    
    # Fix for Windows multiprocessing issues
    import platform
    if platform.system() == 'Windows':
        config.num_workers = 0  # Disable multiprocessing on Windows
    else:
        # Reduce workers if on CPU
        if config.device == 'cpu':
            config.num_workers = 2
    
    return config

def test_prerequisites():
    """Test that everything is set up correctly"""
    print("Testing prerequisites...")
    
    # Test PyTorch
    try:
        import torch
        print(f"✓ PyTorch {torch.__version__} installed")
        if torch.cuda.is_available():
            print(f"✓ CUDA available: {torch.cuda.get_device_name(0)}")
        else:
            print("⚠ CUDA not available, will use CPU")
    except ImportError:
        print("✗ PyTorch not installed - run: pip install torch")
        return False
    
    # Test python-chess
    try:
        import chess
        print(f"✓ python-chess installed")
    except ImportError:
        print("✗ python-chess not installed - run: pip install python-chess")
        return False
    
    # Test database connection
    try:
        config = create_quick_config()
        db = DatabaseConnection(config.connection_string)
        # Quick test - just try to connect without loading data
        import pyodbc
        conn = pyodbc.connect(config.connection_string)
        cursor = conn.cursor()
        cursor.execute("SELECT COUNT(*) FROM ChessMoves WHERE MoveNumber >= 16")
        count = cursor.fetchone()[0]
        conn.close()
        
        print(f"✓ Database connection successful")
        print(f"✓ Available training positions: {count:,}")
        
        if count < 10000:
            print("⚠ Warning: Less than 10,000 positions available")
            print("  Consider generating more games or reducing min_ply")
        
        return True
        
    except Exception as e:
        print(f"✗ Database connection failed: {e}")
        print("  Make sure SQL Server is running and database exists")
        return False

def main():
    """Main quick start function"""
    print("=" * 60)
    print("NNUE Quick Start Training")
    print("=" * 60)
    print()
    
    # Test prerequisites
    if not test_prerequisites():
        print("\nPlease fix the issues above before continuing.")
        return 1
    
    print("\n" + "=" * 60)
    print("CONFIGURATION")
    print("=" * 60)
    
    # Create and show configuration
    config = create_quick_config()
    
    print(f"Device: {config.device}")
    print(f"Dataset size: {config.max_positions:,} positions")
    print(f"Epochs: {config.epochs}")
    print(f"Batch size: {config.batch_size}")
    print(f"Hidden size: {config.hidden_size}")
    print(f"Learning rate: {config.learning_rate}")
    print(f"Model name: {config.model_name}")
    
    # Validate configuration
    issues = validate_config(config)
    if issues:
        print("\nConfiguration issues:")
        for issue in issues:
            print(f"  - {issue}")
    
    # Estimate time
    positions_per_second = 10000 if config.device == 'cuda' else 2000
    estimated_time = (config.max_positions * config.epochs) / positions_per_second / 60
    print(f"\nEstimated training time: {estimated_time:.1f} minutes")
    
    print("\n" + "=" * 60)
    print("STARTING TRAINING")
    print("=" * 60)
    
    # Confirm start
    response = input("\nStart quick training? (y/n): ").lower().strip()
    if response != 'y':
        print("Training cancelled.")
        return 0
    
    # Create output directory
    Path(config.output_dir).mkdir(exist_ok=True)
    
    # Start training
    try:
        trainer = NNUETrainer(config)
        trainer.train()
        
        print("\n" + "=" * 60)
        print("TRAINING COMPLETED!")
        print("=" * 60)
        
        # Show results
        model_path = Path(config.output_dir) / f"{config.model_name}_best.pth"
        if model_path.exists():
            print(f"\nBest model saved to: {model_path}")
            print("\nNext steps:")
            print(f"1. Evaluate model: python model_eval.py {model_path} --benchmark")
            print(f"2. Export for engine: python model_eval.py {model_path} --export-nnue my_model.nnue")
            print("3. Test in your chess engine!")
        
        return 0
        
    except KeyboardInterrupt:
        print("\n\nTraining interrupted by user.")
        return 1
    except Exception as e:
        print(f"\n\nTraining failed: {e}")
        print("\nCommon solutions:")
        print("- Reduce batch_size if GPU memory error")
        print("- Check database connection")
        print("- Ensure sufficient disk space")
        return 1

if __name__ == "__main__":
    sys.exit(main())