using LinkedInContentAutomationSystem.Data;
using LinkedInContentAutomationSystem.PlaywrightAutomation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LinkedInContentAutomationSystem.BackgroundJobs
{
    public class PublishingJob
    {
        private readonly AppDbContext _dbContext;
        private readonly LinkedInPublisher _publisher;
        private readonly ILogger<PublishingJob> _logger;

        public PublishingJob(AppDbContext dbContext, LinkedInPublisher publisher, ILogger<PublishingJob> logger)
        {
            _dbContext = dbContext;
            _publisher = publisher;
            _logger = logger;
        }

        public async Task ExecuteAsync()
        {
            _logger.LogInformation("PublishingJob started.");

            // 1. Transaction Safety
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            // Find next pending post where ScheduledDate <= Today
            var postToPublish = await _dbContext.ScheduledPosts
                .Where(p => p.Status == "Pending" && p.ScheduledDate <= DateTime.Today)
                .OrderBy(p => p.ScheduledDate)
                .FirstOrDefaultAsync();

            if (postToPublish == null)
            {
                _logger.LogInformation("No pending posts scheduled for today or earlier.");
                return;
            }

            // 2. Atomic Lock to prevent duplicate Hangfire workers from picking the same post
            postToPublish.Status = "Processing";
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Attempting to publish post for Day {DayNumber}", postToPublish.DayNumber);

            try
            {
                // 3. Timeout Protection for Playwright
                var publishTask = _publisher.PublishPostAsync(postToPublish.ContentText, postToPublish.ImagePath, postToPublish.UserId);
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));

                var completedTask = await Task.WhenAny(publishTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("Playwright automation timed out after 5 minutes.");
                }

                // Await the task to ensure exceptions are thrown
                await publishTask;
                
                postToPublish.Status = "Posted";
                postToPublish.PostedAt = DateTime.Now;
                postToPublish.ErrorMessage = null;
                
                _logger.LogInformation("Successfully published post for Day {DayNumber}", postToPublish.DayNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish post for Day {DayNumber}", postToPublish.DayNumber);
                
                // 4. Null-Safe Retry Count
                postToPublish.RetryCount++;
                postToPublish.ErrorMessage = ex.Message;
                
                if (postToPublish.RetryCount >= 3)
                {
                    postToPublish.Status = "Failed";
                }
                else
                {
                    postToPublish.Status = "Pending"; // Revert so it can be retried later
                }
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
