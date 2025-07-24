#!/usr/bin/env python3
"""
Test script to verify all imports work correctly
Run this to check if the fixes resolved the import issues
"""

import sys
import os

def test_imports():
    """Test all the important imports"""
    print("Testing NNUE imports...")
    
    try:
        # Test nnue_trainer imports
        print("Testing nnue_trainer imports...", end=" ")
        from nnue_trainer import NNUE, TrainingConfig, HalfKPFeatures, DatabaseConnection, NNUETrainer
        print("✓")
        
        # Test model_eval imports  
        print("Testing model_eval imports...", end=" ")
        from model_eval import ModelEvaluator, NNUEExporter
        print("✓")
        
        # Test training_config imports
        print("Testing training_config imports...", end=" ")
        from training_config import get_training_config, validate_config, TRAINING_PRESETS
        print("✓")
        
        # Test main NNUE imports
        print("Testing main NNUE imports...", end=" ")
        import NNUE as nnue_main
        print("✓")
        
        print("\n" + "="*50)
        print("ALL IMPORTS SUCCESSFUL!")
        print("="*50)
        
        # Test basic functionality
        print("\nTesting basic functionality...")
        
        # Test config creation
        config = get_training_config('default')
        print(f"✓ Created config with device: {config.device}")
        
        # Test validation
        issues = validate_config(config)
        if issues:
            print(f"⚠ Config issues: {len(issues)}")
            for issue in issues:
                print(f"  - {issue}")
        else:
            print("✓ Config validation passed")
        
        # Test feature encoder
        feature_encoder = HalfKPFeatures()
        test_fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
        white_features, black_features = feature_encoder.position_to_features(test_fen)
        print(f"✓ Feature encoding works (white: {white_features.sum()}, black: {black_features.sum()})")
        
        print("\n" + "="*50)
        print("BASIC FUNCTIONALITY TEST PASSED!")
        print("="*50)
        print("\nYou can now run:")
        print("  python NNUE.py                    # Interactive mode")
        print("  python NNUE.py --test-connection  # Test database")
        print("  python quick_start.py             # Quick training test")
        
        return True
        
    except ImportError as e:
        print(f"\n✗ Import failed: {e}")
        print("\nThis usually means:")
        print("1. Missing dependencies - run: pip install -r requirements.txt")
        print("2. Wrong working directory - make sure you're in the NNUE folder")
        print("3. Python path issues")
        return False
        
    except Exception as e:
        print(f"\n✗ Unexpected error: {e}")
        return False

def test_dependencies():
    """Test if required dependencies are installed"""
    print("Testing dependencies...")
    
    required_modules = [
        ('torch', 'PyTorch'),
        ('chess', 'python-chess'), 
        ('numpy', 'NumPy'),
        ('tqdm', 'tqdm'),
        ('pyodbc', 'pyodbc (for SQL Server)')
    ]
    
    missing = []
    for module, name in required_modules:
        try:
            __import__(module)
            print(f"✓ {name}")
        except ImportError:
            print(f"✗ {name} - run: pip install {module}")
            missing.append(module)
    
    if missing:
        print(f"\nMissing dependencies: {', '.join(missing)}")
        print("Run: pip install -r requirements.txt")
        return False
    
    return True

def main():
    print("NNUE Import and Functionality Test")
    print("="*50)
    
    # Test dependencies first
    if not test_dependencies():
        print("\nPlease install missing dependencies first.")
        return 1
    
    print()
    
    # Test imports
    if not test_imports():
        print("\nImport test failed. Please check the error messages above.")
        return 1
    
    print("\n🎉 All tests passed! The NNUE system is ready to use.")
    return 0

if __name__ == "__main__":
    sys.exit(main())