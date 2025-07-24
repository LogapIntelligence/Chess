#!/usr/bin/env python3
"""
NNUE - Main module for chess position evaluation training
"""

# Import all the main classes and functions
from nnue_trainer import (
    NNUE,
    TrainingConfig,
    HalfKPFeatures,
    DatabaseConnection,
    ChessDataset,  
    NNUETrainer
)

from model_eval import (
    ModelEvaluator,
    NNUEExporter
)

from training_config import (
    get_training_config,
    validate_config
)

# Version info
__version__ = "1.0.0"
__author__ = "Chess NNUE Training System"

def test_cuda_support():
    """Test CUDA availability and provide installation guidance"""
    print("\n" + "=" * 60)
    print("           CUDA Support Test")
    print("=" * 60)
    
    try:
        import torch
        print(f"✓ PyTorch version: {torch.__version__}")
        
        if torch.cuda.is_available():
            print("✓ CUDA is available!")
            print(f"  - CUDA version: {torch.version.cuda}")
            print(f"  - cuDNN version: {torch.backends.cudnn.version()}")
            print(f"  - Available GPUs: {torch.cuda.device_count()}")
            
            for i in range(torch.cuda.device_count()):
                gpu_name = torch.cuda.get_device_name(i)
                gpu_memory = torch.cuda.get_device_properties(i).total_memory / 1024**3
                print(f"    GPU {i}: {gpu_name} ({gpu_memory:.1f} GB)")
            
            # Test GPU memory allocation
            try:
                test_tensor = torch.randn(1000, 1000).cuda()
                print("✓ GPU memory allocation test passed")
                del test_tensor
                torch.cuda.empty_cache()
            except Exception as e:
                print(f"✗ GPU memory allocation test failed: {e}")
                
        else:
            print("✗ CUDA is not available")
            print("\nPossible reasons:")
            print("1. PyTorch was installed without CUDA support")
            print("2. NVIDIA drivers are not installed or outdated")
            print("3. CUDA toolkit is not installed")
            print("4. No compatible NVIDIA GPU found")
            
            print("\n" + "=" * 60)
            print("CUDA Installation Guide:")
            print("=" * 60)
            show_cuda_installation_guide()
            
    except ImportError:
        print("✗ PyTorch is not installed")
        print("\nInstall PyTorch first, then run this test again.")
        show_pytorch_installation_guide()
    
    print("\n" + "=" * 60)
    input("Press Enter to continue...")

def show_cuda_installation_guide():
    """Show detailed CUDA installation instructions"""
    print("\n1. Check your NVIDIA GPU compatibility:")
    print("   Visit: https://developer.nvidia.com/cuda-gpus")
    print("   Minimum: GTX 750 Ti or newer, Compute Capability 3.5+")
    
    print("\n2. Install NVIDIA drivers:")
    print("   Windows: Download from nvidia.com/drivers")
    print("   Linux: sudo apt install nvidia-driver-xxx (replace xxx with version)")
    print("   Check with: nvidia-smi")
    
    print("\n3. Install CUDA Toolkit (optional for PyTorch):")
    print("   Download from: https://developer.nvidia.com/cuda-downloads")
    print("   Note: PyTorch includes its own CUDA runtime")
    
    print("\n4. Install PyTorch with CUDA support:")
    show_pytorch_installation_guide()

def show_pytorch_installation_guide():
    """Show PyTorch installation commands for different CUDA versions"""
    print("\nPyTorch Installation Commands:")
    print("-----------------------------")
    print("For CUDA 11.8:")
    print("  pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118")
    print("\nFor CUDA 12.1:")
    print("  pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu121")
    print("\nFor CPU only:")
    print("  pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cpu")
    print("\nCheck compatibility at: https://pytorch.org/get-started/locally/")

def show_interactive_menu():
    """Show interactive menu when no arguments are passed"""
    print("=" * 60)
    print("           NNUE Chess Training System")
    print("=" * 60)
    print()
    print("Available options:")
    print("1. Test database connection")
    print("2. Test CUDA support")
    print("3. Quick training test (small dataset)")
    print("4. Start full training")
    print("5. Evaluate existing model")
    print("6. Show training configurations")
    print("7. Exit")
    print()
    
    while True:
        try:
            choice = input("Enter your choice (1-7): ").strip()
            
            if choice == '1':
                return ['--test-connection']
            elif choice == '2':
                return ['--test-cuda']
            elif choice == '3':
                return ['--config', 'quick_test', '--train']
            elif choice == '4':
                return ['--config', 'default', '--train']
            elif choice == '5':
                model_path = input("Enter model path (.pth file): ").strip()
                if model_path:
                    return ['--eval', model_path, '--benchmark']
                else:
                    print("Model path required!")
                    continue
            elif choice == '6':
                show_configurations()
                continue
            elif choice == '7':
                print("Goodbye!")
                return None
            else:
                print("Invalid choice. Please enter 1-7.")
                continue
                
        except KeyboardInterrupt:
            print("\nGoodbye!")
            return None

def show_configurations():
    """Show available training configurations"""
    print("\n" + "=" * 50)
    print("Available Training Configurations:")
    print("=" * 50)
    
    configs = {
        'default': 'Standard training (1M positions, 100 epochs)',
        'quick_test': 'Quick test (100K positions, 20 epochs)',
        'large_scale': 'Large scale (5M positions, 200 epochs)',
        'cpu_only': 'CPU optimized (500K positions, smaller batches)'
    }
    
    for name, desc in configs.items():
        print(f"  {name:12s}: {desc}")
    
    print("\nUse with: python NNUE.py --config <name> --train")
    print()

def main():
    """Main entry point for NNUE training"""
    import argparse
    import sys
    
    # If no arguments provided, show interactive menu
    if len(sys.argv) == 1:
        args_list = show_interactive_menu()
        if args_list is None:
            return 0
        # Simulate command line arguments
        sys.argv.extend(args_list)
    
    parser = argparse.ArgumentParser(description="NNUE Chess Evaluation Training")
    parser.add_argument('--train', action='store_true', help='Start training')
    parser.add_argument('--eval', type=str, help='Evaluate model from path')
    parser.add_argument('--config', type=str, default='default', help='Training configuration preset')
    parser.add_argument('--test-connection', action='store_true', help='Test database connection')
    parser.add_argument('--test-cuda', action='store_true', help='Test CUDA support and installation')
    
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
    
    if args.test_cuda:
        test_cuda_support()
        return 0
    
    elif args.test_connection:
        print("Testing database connection...")
        config = get_training_config(args.config)
        db = DatabaseConnection(config.connection_string)
        try:
            positions = db.load_training_data(config)
            print(f"✓ Database connection successful! Found {len(positions)} positions")
            return 0
        except Exception as e:
            print(f"✗ Database connection failed: {e}")
            print("\nCommon solutions:")
            print("1. Make sure SQL Server is running")
            print("2. Check connection string in training_config.py")
            print("3. Ensure ODBC Driver 17 is installed")
            print("4. Verify ChessDatabase exists with data")
            return 1
    
    elif args.eval:
        # Model evaluation mode
        print(f"Loading model from: {args.eval}")
        try:
            evaluator = ModelEvaluator(args.eval)
            
            if args.benchmark:
                print("Running speed benchmark...")
                evaluator.benchmark_speed()
            
            if args.compare_engine:
                # Load some test positions
                config = get_training_config(args.config)
                db = DatabaseConnection(config.connection_string)
                positions = db.load_training_data(config)
                test_positions = [pos[0] for pos in positions[:100]]
                evaluator.compare_with_engine(args.compare_engine, test_positions)
            
            if args.export_nnue:
                print(f"Exporting to NNUE format: {args.export_nnue}")
                exporter = NNUEExporter(evaluator.model, evaluator.config)
                exporter.export_stockfish_nnue(args.export_nnue)
            
            print("Evaluation completed!")
            
        except Exception as e:
            print(f"Error during evaluation: {e}")
            return 1
    
    elif args.train:
        # Training mode
        print(f"Starting training with config: {args.config}")
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
        
        # Show configuration
        print("\nTraining Configuration:")
        print(f"  Device: {config.device}")
        print(f"  Max positions: {config.max_positions:,}")
        print(f"  Batch size: {config.batch_size}")
        print(f"  Epochs: {config.epochs}")
        print(f"  Learning rate: {config.learning_rate}")
        print(f"  Model name: {config.model_name}")
        print()
        
        # Start training
        try:
            trainer = NNUETrainer(config)
            trainer.train()
            print("Training completed successfully!")
        except Exception as e:
            print(f"Training failed: {e}")
            return 1
    
    else:
        # Show help if no action specified
        parser.print_help()
        return 0
    
    return 0

if __name__ == "__main__":
    import sys
    sys.exit(main())