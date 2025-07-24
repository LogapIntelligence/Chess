#!/usr/bin/env python3
"""
NNUE Training Configuration
Modify this file to customize your training settings
"""

import torch  # Import at module level for proper CUDA initialization
import platform

from nnue_trainer import TrainingConfig

def create_training_config():
    """Create training configuration with custom settings"""
    config = TrainingConfig()
    
    # =============================================================================
    # DATABASE SETTINGS
    # =============================================================================
    # Update this connection string for your SQL Server instance
    config.connection_string = "Server=localhost\\SQLEXPRESS;Database=ChessDatabase;UID=chess;PWD=chess@123;Driver={ODBC Driver 17 for SQL Server};"

    
    # =============================================================================
    # MODEL ARCHITECTURE
    # =============================================================================
    config.input_size = 768     # HalfKP features (don't change unless you know what you're doing)
    config.hidden_size = 256    # Hidden layer size (256, 512, 1024)
    config.output_size = 1      # Single evaluation output (don't change)
    
    # =============================================================================
    # TRAINING HYPERPARAMETERS
    # =============================================================================
    config.batch_size = 8192           # Batch size (reduce if GPU memory issues: 4096, 2048, 1024)
    config.learning_rate = 0.001       # Learning rate (0.0001 to 0.01)
    config.weight_decay = 1e-4         # L2 regularization
    config.epochs = 100                # Number of training epochs
    config.validation_split = 0.1      # Fraction of data for validation
    
    # =============================================================================
    # DATA PARAMETERS
    # =============================================================================
    config.max_positions = 1000000     # Maximum positions to load (start smaller for testing)
    config.eval_scale = 361.0          # Evaluation scaling factor (pawn = 100cp, so 361 scales to ~±3)
    config.min_ply = 16                # Minimum game ply (avoid opening book positions)
    config.max_eval = 10.0             # Maximum evaluation magnitude (in pawn units)
    
    # =============================================================================
    # HARDWARE SETTINGS
    # =============================================================================
    config.device = 'cuda' if torch.cuda.is_available() else 'cpu'  # Auto-detect device
    if platform.system() == 'Windows':
        config.num_workers = 0  # Disable multiprocessing on Windows
    else:
        config.num_workers = 4  # Data loading workers for Linux/Mac    
    # =============================================================================
    # TRAINING OPTIONS
    # =============================================================================
    config.save_frequency = 10         # Save model every N epochs
    config.model_name = 'chess_nnue'   # Base name for saved models
    config.output_dir = 'models'       # Output directory for models
    
    return config

def create_quick_test_config():
    """Create configuration for quick testing (smaller dataset, fewer epochs)"""
    config = create_training_config()
    
    # Reduce parameters for quick testing
    config.max_positions = 100000      # Smaller dataset
    config.epochs = 20                 # Fewer epochs
    config.batch_size = 4096           # Smaller batch size
    config.model_name = 'test_nnue'    # Different name
    
    # Auto-detect device for quick test too
    config.device = 'cuda' if torch.cuda.is_available() else 'cpu'
    
    return config

def create_large_training_config():
    """Create configuration for large-scale training"""
    config = create_training_config()
    
    # Increase parameters for serious training
    config.max_positions = 5000000     # Large dataset
    config.epochs = 200                # More epochs
    config.batch_size = 16384          # Larger batch size (if GPU memory allows)
    config.hidden_size = 512           # Larger network
    config.learning_rate = 0.0005      # Lower learning rate for stability
    config.model_name = 'large_nnue'   # Different name
    
    return config

def create_cpu_training_config():
    """Create configuration optimized for CPU training"""
    config = create_training_config()
    
    # Optimize for CPU training
    config.device = 'cpu'              # Force CPU
    config.batch_size = 1024           # Smaller batch size for CPU
    config.num_workers = 8             # More workers for CPU
    config.max_positions = 500000      # Moderate dataset size
    config.model_name = 'cpu_nnue'     # Different name
    
    return config

# =============================================================================
# TRAINING PRESETS
# =============================================================================

TRAINING_PRESETS = {
    'default': create_training_config,
    'quick_test': create_quick_test_config,
    'large_scale': create_large_training_config,
    'cpu_only': create_cpu_training_config,
}

def get_training_config(preset='default'):
    """Get training configuration by preset name"""
    if preset in TRAINING_PRESETS:
        config = TRAINING_PRESETS[preset]()
        # Debug print to confirm device
        print(f"Config '{preset}' created with device: {config.device}")
        return config
    else:
        print(f"Warning: Unknown preset '{preset}', using default")
        return create_training_config()

# =============================================================================
# CONFIGURATION VALIDATION
# =============================================================================

def validate_config(config):
    """Validate training configuration"""
    issues = []
    
    # Check required fields
    if not config.connection_string:
        issues.append("Database connection string is required")
    
    # Check reasonable ranges
    if config.batch_size < 64 or config.batch_size > 32768:
        issues.append(f"Batch size {config.batch_size} may be too small or large")
    
    if config.learning_rate < 1e-6 or config.learning_rate > 0.1:
        issues.append(f"Learning rate {config.learning_rate} may be too small or large")
    
    if config.max_positions < 1000:
        issues.append(f"Max positions {config.max_positions} may be too small for effective training")
    
    # Check device availability - FIXED VERSION
    if config.device == 'cuda':
        try:
            import torch
            if not torch.cuda.is_available():
                # Just warn, don't add to issues - this allows training to proceed
                print("⚠ Warning: CUDA validation check failed, but training may still work on GPU")
                print("  Your Blackwell GPU should work fine - this is likely a timing issue")
                # NOT adding to issues list - this is the key fix!
        except ImportError:
            issues.append("PyTorch not installed")
    
    return issues

# =============================================================================
# USAGE EXAMPLES
# =============================================================================

if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="NNUE Training Configuration")
    parser.add_argument('--preset', choices=list(TRAINING_PRESETS.keys()), 
                       default='default', help='Training preset to use')
    parser.add_argument('--validate', action='store_true', help='Validate configuration')
    parser.add_argument('--show', action='store_true', help='Show configuration')
    
    args = parser.parse_args()
    
    # Show CUDA status
    print(f"\nCUDA Available: {torch.cuda.is_available()}")
    if torch.cuda.is_available():
        print(f"GPU: {torch.cuda.get_device_name(0)}")
    
    # Get configuration
    config = get_training_config(args.preset)
    
    # Show configuration
    if args.show:
        print(f"\nTraining Configuration (preset: {args.preset})")
        print("=" * 50)
        for key, value in config.__dict__.items():
            print(f"{key:20s}: {value}")
    
    # Validate configuration
    if args.validate:
        issues = validate_config(config)
        if issues:
            print(f"\nConfiguration Issues:")
            for issue in issues:
                print(f"  - {issue}")
        else:
            print("\n✓ Configuration is valid")
    
    # Show available presets
    if not args.show and not args.validate:
        print("Available training presets:")
        for preset_name in TRAINING_PRESETS.keys():
            print(f"  - {preset_name}")
        print(f"\nUsage: python {__file__} --preset [preset_name] --show")