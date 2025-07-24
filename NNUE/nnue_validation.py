#!/usr/bin/env python3
"""
NNUE Validation Script
Tests training with different configurations to identify issues
Compatible with PyTorch 2.9.0 development version
"""

import os
import sys
import time
import torch
import traceback
from pathlib import Path

# Add current directory to path
sys.path.insert(0, os.path.dirname(__file__))

def test_pytorch_version():
    """Test PyTorch version and capabilities"""
    print("\n" + "="*60)
    print("PyTorch Version Check")
    print("="*60)
    
    print(f"PyTorch Version: {torch.__version__}")
    print(f"CUDA Available: {torch.cuda.is_available()}")
    
    if torch.cuda.is_available():
        print(f"CUDA Version: {torch.version.cuda}")
        print(f"GPU Device: {torch.cuda.get_device_name(0)}")
        print(f"GPU Memory: {torch.cuda.get_device_properties(0).total_memory / 1024**3:.1f} GB")
        
        # Test basic GPU operations
        try:
            test_tensor = torch.randn(1000, 1000).cuda()
            result = torch.matmul(test_tensor, test_tensor)
            print("✓ GPU tensor operations working")
            del test_tensor, result
            torch.cuda.empty_cache()
        except Exception as e:
            print(f"✗ GPU tensor operations failed: {e}")
    
    # Check for known issues with dev version
    print("\n⚠ Warning: Using PyTorch development version (2.9.0.dev)")
    print("  Some features may be unstable or have breaking changes")

def test_imports():
    """Test all required imports"""
    print("\n" + "="*60)
    print("Import Tests")
    print("="*60)
    
    modules = [
        ('torch', 'PyTorch'),
        ('numpy', 'NumPy'),
        ('chess', 'python-chess'),
        ('pyodbc', 'pyodbc'),
        ('tqdm', 'tqdm'),
        ('matplotlib', 'matplotlib'),
    ]
    
    all_good = True
    for module, name in modules:
        try:
            __import__(module)
            print(f"✓ {name}")
        except ImportError as e:
            print(f"✗ {name}: {e}")
            all_good = False
    
    # Test local imports
    try:
        from nnue_trainer import NNUE, TrainingConfig, HalfKPFeatures, DatabaseConnection, NNUETrainer
        print("✓ nnue_trainer imports")
    except ImportError as e:
        print(f"✗ nnue_trainer imports: {e}")
        all_good = False
    
    return all_good

def validate_small_config():
    """Validate small test configuration"""
    print("\n" + "="*60)
    print("Small Configuration Validation")
    print("="*60)
    
    try:
        from nnue_trainer import TrainingConfig
        from training_config import validate_config
        
        config = TrainingConfig()
        config.max_positions = 10000      # Very small for quick test
        config.epochs = 5                 # Just a few epochs
        config.batch_size = 1024          # Small batch size
        config.num_workers = 0            # Disable multiprocessing for test
        config.save_frequency = 2         # Save frequently
        config.model_name = "validation_small"
        
        # Show configuration
        print("Configuration:")
        print(f"  Device: {config.device}")
        print(f"  Positions: {config.max_positions}")
        print(f"  Epochs: {config.epochs}")
        print(f"  Batch size: {config.batch_size}")
        print(f"  Workers: {config.num_workers}")
        
        # Validate
        issues = validate_config(config)
        if issues:
            print("\nValidation issues:")
            for issue in issues:
                print(f"  - {issue}")
        else:
            print("\n✓ Configuration valid")
        
        return config, len(issues) == 0
        
    except Exception as e:
        print(f"\n✗ Configuration validation failed: {e}")
        traceback.print_exc()
        return None, False

def validate_large_config():
    """Validate large test configuration"""
    print("\n" + "="*60)
    print("Large Configuration Validation")
    print("="*60)
    
    try:
        from nnue_trainer import TrainingConfig
        from training_config import validate_config
        
        config = TrainingConfig()
        config.max_positions = 100000     # Medium-large dataset
        config.epochs = 20                # More epochs
        config.batch_size = 8192          # Large batch size
        config.num_workers = 4            # Multiple workers
        config.hidden_size = 512          # Larger network
        config.model_name = "validation_large"
        
        # Show configuration
        print("Configuration:")
        print(f"  Device: {config.device}")
        print(f"  Positions: {config.max_positions}")
        print(f"  Epochs: {config.epochs}")
        print(f"  Batch size: {config.batch_size}")
        print(f"  Hidden size: {config.hidden_size}")
        print(f"  Workers: {config.num_workers}")
        
        # Check GPU memory requirements
        if config.device == 'cuda':
            # Estimate memory usage
            model_params = config.input_size * config.hidden_size + config.hidden_size * 32 + 32 * 32 + 32
            batch_memory = config.batch_size * config.input_size * 4  # float32
            total_memory_mb = (model_params * 4 + batch_memory) / (1024 * 1024)
            
            print(f"\nEstimated GPU memory usage: {total_memory_mb:.1f} MB")
            
            if torch.cuda.is_available():
                available_memory = torch.cuda.get_device_properties(0).total_memory / (1024 * 1024)
                print(f"Available GPU memory: {available_memory:.1f} MB")
                
                if total_memory_mb > available_memory * 0.8:  # 80% threshold
                    print("⚠ Warning: May run out of GPU memory!")
        
        # Validate
        issues = validate_config(config)
        if issues:
            print("\nValidation issues:")
            for issue in issues:
                print(f"  - {issue}")
        else:
            print("\n✓ Configuration valid")
        
        return config, len(issues) == 0
        
    except Exception as e:
        print(f"\n✗ Configuration validation failed: {e}")
        traceback.print_exc()
        return None, False

def test_model_creation(config):
    """Test model creation and basic operations"""
    print("\n" + "="*60)
    print(f"Model Creation Test ({config.model_name})")
    print("="*60)
    
    try:
        from nnue_trainer import NNUE, HalfKPFeatures
        
        # Create model
        model = NNUE(config)
        print(f"✓ Model created")
        print(f"  Parameters: {sum(p.numel() for p in model.parameters()):,}")
        
        # Move to device
        model = model.to(config.device)
        print(f"✓ Model moved to {config.device}")
        
        # Test forward pass
        dummy_input = torch.randn(16, config.input_size).to(config.device)
        output = model(dummy_input)
        print(f"✓ Forward pass successful")
        print(f"  Input shape: {dummy_input.shape}")
        print(f"  Output shape: {output.shape}")
        
        # Test feature encoding
        feature_encoder = HalfKPFeatures()
        test_fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
        white_features, black_features = feature_encoder.position_to_features(test_fen)
        print(f"✓ Feature encoding working")
        print(f"  White features sum: {white_features.sum()}")
        print(f"  Black features sum: {black_features.sum()}")
        
        # Clean up
        del model, dummy_input, output
        if config.device == 'cuda':
            torch.cuda.empty_cache()
        
        return True
        
    except Exception as e:
        print(f"\n✗ Model creation failed: {e}")
        traceback.print_exc()
        return False

def test_optimizer_scheduler(config):
    """Test optimizer and scheduler creation"""
    print("\n" + "="*60)
    print("Optimizer and Scheduler Test")
    print("="*60)
    
    try:
        from nnue_trainer import NNUE
        import torch.optim as optim
        
        # Create model
        model = NNUE(config).to(config.device)
        
        # Create optimizer
        optimizer = optim.AdamW(
            model.parameters(),
            lr=config.learning_rate,
            weight_decay=config.weight_decay
        )
        print("✓ AdamW optimizer created")
        
        # Create scheduler (testing the fixed version without verbose)
        scheduler = optim.lr_scheduler.ReduceLROnPlateau(
            optimizer, mode='min', factor=0.5, patience=5
        )
        print("✓ ReduceLROnPlateau scheduler created (without verbose)")
        
        # Test scheduler step
        scheduler.step(1.0)
        print("✓ Scheduler step successful")
        
        # Clean up
        del model, optimizer, scheduler
        if config.device == 'cuda':
            torch.cuda.empty_cache()
        
        return True
        
    except Exception as e:
        print(f"\n✗ Optimizer/scheduler test failed: {e}")
        traceback.print_exc()
        return False

def test_data_loading(config):
    """Test data loading with minimal dataset"""
    print("\n" + "="*60)
    print("Data Loading Test")
    print("="*60)
    
    try:
        from nnue_trainer import ChessDataset
        from torch.utils.data import DataLoader
        
        # Create minimal test data
        test_positions = [
            ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 0.0, "1/2-1/2"),
            ("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", 0.3, "1-0"),
            ("rnbqkb1r/pppppppp/5n2/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2", 0.2, "1-0"),
        ]
        
        # Create dataset
        dataset = ChessDataset(test_positions, config)
        print(f"✓ Dataset created with {len(dataset)} positions")
        
        # Test single item
        features, evaluation, outcome = dataset[0]
        print(f"✓ Single item access working")
        print(f"  Features shape: {features.shape}")
        print(f"  Evaluation: {evaluation.item():.3f}")
        print(f"  Outcome: {outcome.item():.3f}")
        
        # Create DataLoader with num_workers=0 for testing
        loader = DataLoader(
            dataset,
            batch_size=2,
            shuffle=True,
            num_workers=0  # Always use 0 for testing
        )
        print("✓ DataLoader created")
        
        # Test iteration
        for batch in loader:
            features_batch, eval_batch, outcome_batch = batch
            print(f"✓ Batch iteration working")
            print(f"  Batch features shape: {features_batch.shape}")
            break
        
        return True
        
    except Exception as e:
        print(f"\n✗ Data loading test failed: {e}")
        traceback.print_exc()
        return False

def run_mini_training(config):
    """Run a mini training session to test the full pipeline"""
    print("\n" + "="*60)
    print(f"Mini Training Test ({config.model_name})")
    print("="*60)
    
    try:
        from nnue_trainer import NNUE, ChessDataset, NNUETrainer
        from torch.utils.data import DataLoader
        import torch.nn as nn
        import torch.optim as optim
        
        # Create minimal dataset
        test_positions = []
        for i in range(100):  # Create 100 random positions
            fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
            evaluation = float(i % 10 - 5) * 0.1  # Random evaluations
            result = ["1-0", "0-1", "1/2-1/2"][i % 3]
            test_positions.append((fen, evaluation, result))
        
        # Split data
        train_positions = test_positions[:80]
        val_positions = test_positions[80:]
        
        # Create datasets and loaders
        train_dataset = ChessDataset(train_positions, config)
        val_dataset = ChessDataset(val_positions, config)
        
        train_loader = DataLoader(train_dataset, batch_size=16, shuffle=True, num_workers=0)
        val_loader = DataLoader(val_dataset, batch_size=16, shuffle=False, num_workers=0)
        
        # Create model and training components
        model = NNUE(config).to(config.device)
        optimizer = optim.AdamW(model.parameters(), lr=0.001)
        scheduler = optim.lr_scheduler.ReduceLROnPlateau(optimizer, mode='min', factor=0.5, patience=2)
        criterion = nn.MSELoss()
        
        print("✓ Training components created")
        
        # Run 2 epochs
        for epoch in range(2):
            # Training
            model.train()
            train_loss = 0
            for features, evaluations, _ in train_loader:
                features = features.to(config.device)
                evaluations = evaluations.to(config.device)
                
                optimizer.zero_grad()
                predictions = model(features)
                loss = criterion(predictions, evaluations)
                loss.backward()
                optimizer.step()
                
                train_loss += loss.item()
            
            train_loss /= len(train_loader)
            
            # Validation
            model.eval()
            val_loss = 0
            with torch.no_grad():
                for features, evaluations, _ in val_loader:
                    features = features.to(config.device)
                    evaluations = evaluations.to(config.device)
                    
                    predictions = model(features)
                    loss = criterion(predictions, evaluations)
                    val_loss += loss.item()
            
            val_loss /= len(val_loader)
            
            scheduler.step(val_loss)
            
            print(f"  Epoch {epoch+1}: Train Loss={train_loss:.4f}, Val Loss={val_loss:.4f}")
        
        print("✓ Mini training completed successfully!")
        
        # Clean up
        del model, optimizer, scheduler
        if config.device == 'cuda':
            torch.cuda.empty_cache()
        
        return True
        
    except Exception as e:
        print(f"\n✗ Mini training failed: {e}")
        traceback.print_exc()
        return False

def main():
    """Main validation function"""
    print("="*60)
    print("NNUE Training Validation Suite")
    print("="*60)
    print("\nThis will validate the NNUE training system with PyTorch 2.9.0")
    
    # Create output directory
    Path("models").mkdir(exist_ok=True)
    
    # Run tests
    results = {}
    
    # Basic tests
    test_pytorch_version()
    results['imports'] = test_imports()
    
    if not results['imports']:
        print("\n✗ Import test failed. Cannot continue.")
        return 1
    
    # Configuration tests
    small_config, results['small_config'] = validate_small_config()
    large_config, results['large_config'] = validate_large_config()
    
    # Component tests with small config
    if small_config and results['small_config']:
        results['model_small'] = test_model_creation(small_config)
        results['optimizer'] = test_optimizer_scheduler(small_config)
        results['data_loading'] = test_data_loading(small_config)
        results['mini_training'] = run_mini_training(small_config)
    
    # Component tests with large config (if GPU available)
    if large_config and results['large_config'] and torch.cuda.is_available():
        results['model_large'] = test_model_creation(large_config)
    
    # Summary
    print("\n" + "="*60)
    print("VALIDATION SUMMARY")
    print("="*60)
    
    for test_name, passed in results.items():
        status = "✓ PASSED" if passed else "✗ FAILED"
        print(f"{test_name:20s}: {status}")
    
    all_passed = all(results.values())
    
    if all_passed:
        print("\n🎉 All validation tests passed!")
        print("\nYou can now run:")
        print("  python quick_start.py     # For quick training")
        print("  python NNUE.py --train    # For full training")
    else:
        print("\n⚠ Some tests failed. Please check the errors above.")
        print("\nCommon fixes:")
        print("  - Use the fixed nnue_trainer.py file")
        print("  - Reduce batch_size if GPU memory issues")
        print("  - Set num_workers=0 if multiprocessing issues")
    
    return 0 if all_passed else 1

if __name__ == "__main__":
    sys.exit(main())