using Database.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;

namespace Database.ViewModels
{
    public class CreateBatchViewModel
    {
        [Required]
        [Display(Name = "Chess Engine")]
        public long EngineId { get; set; }

        [Required]
        [Range(1, 60000)] // 100ms to 60 seconds
        [Display(Name = "Move Time (ms)")]
        public long MovetimeMs { get; set; } = 50;

        [Required]
        [Range(1, 10000)]
        [Display(Name = "Number of Games")]
        public long TotalGames { get; set; } = 1000;

        // Engine parameters
        [Range(1, 128)]
        [Display(Name = "Threads")]
        public int Threads { get; set; } = 1;

        [Range(16, 32768)]
        [Display(Name = "Hash Size (MB)")]
        public int HashSizeMB { get; set; } = 512;

        [Display(Name = "MultiPV")]
        [Range(1, 5)]
        public int MultiPV { get; set; } = 1;

        [Display(Name = "Use NNUE")]
        public bool UseNNUE { get; set; } = true;

        [Display(Name = "Contempt")]
        [Range(-100, 100)]
        public int Contempt { get; set; } = 0;

        [BindNever]
        public IEnumerable<Engine> Engines { get; set; }
    }

    public class UploadEngineViewModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [Display(Name = "Engine File")]
        public IFormFile EngineFile { get; set; }
    }
}
