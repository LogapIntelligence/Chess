#!/usr/bin/env python3
"""
Setup and configuration script for NNUE training
"""

import os
import sys
import subprocess
import platform
from pathlib import Path

def check_python_version():
    """Check if Python version is compatible"""
    if sys.version_info < (3, 8):
        print("Error: Python 3.8 or higher is required")
        sys.exit(1)
    print(f"✓ Python {sys.version_info.major}.{sys.version_info.minor} detected")

def check_cuda_availability():
    """Check CUDA availability"""
    try:
        import torch
        if torch.cuda.is_available():
            print(f"✓ CUDA available: {torch.cuda.get_device_name(0)}")
            print(f"  CUDA version: {torch.version.cuda}")
            print(f"  Available devices: {torch.cuda.device_count()}")
            return True
        else:
            print("⚠ CUDA not available, will use CPU training")
            return False
    except ImportError:
        print("⚠ PyTorch not installed yet")
        return False

def install_requirements():
    """Install Python requirements"""
    print("Installing Python requirements...")
    
    requirements_file = Path("requirements.txt")
    if requirements_file.exists():
        try:
            subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", "requirements.txt"])
            print("✓ Requirements installed successfully")
        except subprocess.CalledProcessError as e:
            print(f"Error installing requirements: {e}")
            sys.exit(1)
    else:
        print("Error: requirements.txt not found")
        sys.exit(1)

def setup_directories():
    """Create necessary directories"""
    directories = ["models", "logs", "data", "exports"]
    for directory in directories:
        Path(directory).mkdir(exist_ok=True)
        print(f"✓ Created directory: {directory}")

def check_database_connection():
    """Test database connection"""
    print("Testing database connection...")
    
    try:
        import pyodbc
        
        # Default connection string - user should modify as needed
        connection_string = (
            "Server=localhost\\SQLEXPRESS;"
            "Database=ChessDatabase;"
            "Trusted_Connection=True;"
            "Driver={ODBC Driver 17 for SQL Server};"
        )
        
        conn = pyodbc.connect(connection_string)
        cursor = conn.cursor()
        
        # Test query
        cursor.execute("SELECT COUNT(*) FROM ChessMoves")
        move_count = cursor.fetchone()[0]
        
        cursor.execute("SELECT COUNT(*) FROM ChessGames")
        game_count = cursor.fetchone()[0]
        
        conn.close()
        
        print(f"✓ Database connection successful")
        print(f"  Chess moves: {move_count:,}")
        print(f"  Chess games: {game_count:,}")
        
        return True
        
    except ImportError:
        print("⚠ pyodbc not installed")
        return False
    except Exception as e:
        print(f"⚠ Database connection failed: {e}")
        print("  Make sure SQL Server is running and connection string is correct")
        return False

def create_sample_config():
    """Create sample training configuration"""
    config_content = '''# NNUE Training Configuration
# Copy this to config.py and modify as needed

TRAINING_CONFIG = {
    # Database
    "connection_string": "Server=localhost\\SQLEXPRESS;Database=ChessDatabase;Trusted_Connection=True;Driver={ODBC Driver 17 for SQL Server};",
    
    # Model Architecture
    "input_size": 768,
    "hidden_size": 256,
    "output_size": 1,
    
    # Training Parameters
    "batch_size": 8192,        # Reduce if out of GPU memory
    "learning_rate": 0.001,
    "weight_decay": 1e-4,
    "epochs": 100,
    "validation_split": 0.1,
    
    # Data Parameters
    "max_positions": 1000000,  # Maximum positions to load
    "eval_scale": 361.0,       # Evaluation scaling (361 = pawn value)
    "min_ply": 16,            # Minimum game ply for training
    "max_eval": 10.0,         # Maximum evaluation magnitude
    
    # Training Options
    "device": "cuda",         # "cuda" or "cpu"
    "num_workers": 4,         # Data loading workers
    "save_frequency": 10,     # Save every N epochs
    
    # Output
    "model_name": "chess_nnue",
    "output_dir": "models"
}
'''
    
    config_file = Path("sample_config.py")
    with open(config_file, "w") as f:
        f.write(config_content)
    
    print(f"✓ Created sample configuration: {config_file}")

def create_training_scripts():
    """Create convenient training scripts"""
    
    # Quick start script
    quick_start = '''#!/usr/bin/env python3
"""
Quick start training script
Modify parameters as needed
"""

import os
import sys
sys.path.append(os.path.dirname(__file__))

from NNUE import NNUETrainer, TrainingConfig

def main():
    # Basic configuration
    config = TrainingConfig()
    
    # Modify these parameters as needed
    config.batch_size = 4096        # Reduce if GPU memory issues
    config.max_positions = 500000   # Start with smaller dataset
    config.epochs = 50              # Fewer epochs for quick test
    config.model_name = "test_model"
    
    # Start training
    trainer = NNUETrainer(config)
    trainer.train()

if __name__ == "__main__":
    main()
'''
    
    with open("quick_start.py", "w") as f:
        f.write(quick_start)
    
    # Batch training script for Windows
    batch_script = '''@echo off
echo Starting NNUE Training...
echo.

REM Activate virtual environment if needed
REM call venv\\Scripts\\activate.bat

echo Testing database connection...
python NNUE.py --test-connection
if errorlevel 1 (
    echo Database connection failed!
    pause
    exit /b 1
)

echo.
echo Starting training...
python NNUE.py --batch-size 4096 --epochs 50 --max-positions 500000

echo.
echo Training completed!
pause
'''
    
    with open("train.bat", "w") as f:
        f.write(batch_script)
    
    # Shell script for Linux/Mac
    shell_script = '''#!/bin/bash
echo "Starting NNUE Training..."
echo

# Activate virtual environment if needed
# source venv/bin/activate

echo "Testing database connection..."
python3 NNUE.py --test-connection
if [ $? -ne 0 ]; then
    echo "Database connection failed!"
    exit 1
fi

echo
echo "Starting training..."
python3 NNUE.py --batch-size 4096 --epochs 50 --max-positions 500000

echo
echo "Training completed!"
'''
    
    with open("train.sh", "w") as f:
        f.write(shell_script)
    
    # Make shell script executable on Unix systems
    if platform.system() != "Windows":
        os.chmod("train.sh", 0o755)
    
    print("✓ Created training scripts:")
    print("  - quick_start.py (Python)")
    print("  - train.bat (Windows)")
    print("  - train.sh (Linux/Mac)")

def print_next_steps():
    """Print next steps for user"""
    print("\n" + "="*60)
    print("SETUP COMPLETE!")
    print("="*60)
    print("\nNext steps:")
    print("1. Verify your database connection string in the configuration")
    print("2. Start with a small test:")
    print("   python NNUE.py --test-connection")
    print("3. Run quick training test:")
    print("   python quick_start.py")
    print("4. For full training:")
    print("   python NNUE.py --batch-size 8192 --epochs 100")
    print("\nOptional optimizations:")
    print("- Use GPU for faster training (requires CUDA)")
    print("- Increase batch size if you have more GPU memory")
    print("- Filter training data by game quality/rating")
    print("\nOutput files will be saved in:")
    print("- models/     (trained model checkpoints)")
    print("- logs/       (training logs)")
    print("- exports/    (exported .nnue files)")

def main():
    """Main setup function"""
    print("NNUE Training Setup")
    print("="*50)
    
    # Check prerequisites
    check_python_version()
    
    # Install requirements
    install_requirements()
    
    # Check capabilities
    check_cuda_availability()
    
    # Setup directories
    setup_directories()
    
    # Test database
    check_database_connection()
    
    # Create configuration files
    create_sample_config()
    create_training_scripts()
    
    # Show next steps
    print_next_steps()

if __name__ == "__main__":
    main()
