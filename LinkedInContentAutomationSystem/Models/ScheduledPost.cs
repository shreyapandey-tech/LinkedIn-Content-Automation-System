using System.ComponentModel.DataAnnotations;

namespace LinkedInContentAutomationSystem.Models
{
    public class ScheduledPost
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DayNumber { get; set; }

        [Required]
        public string ContentText { get; set; } = string.Empty;

        public string? ImagePath { get; set; }

        /// <summary>
        /// Status: Pending, Posted, Failed
        /// </summary>
        [Required]
        public string Status { get; set; } = "Pending";

        [Required]
        public DateTime ScheduledDate { get; set; }

        public int RetryCount { get; set; } = 0;

        public DateTime? PostedAt { get; set; }

        public string? ErrorMessage { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }
    }
}
