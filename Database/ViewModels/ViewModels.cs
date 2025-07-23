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
        [Range(1, 100)]
        public long Depth { get; set; }

        [Required]
        [Range(1, 10000)]
        [Display(Name = "Number of Games")]
        public long TotalGames { get; set; }
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
