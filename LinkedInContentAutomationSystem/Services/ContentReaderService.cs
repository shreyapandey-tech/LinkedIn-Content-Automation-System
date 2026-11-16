using LinkedInContentAutomationSystem.Data;
using LinkedInContentAutomationSystem.Models;
using Microsoft.Extensions.Logging;

namespace LinkedInContentAutomationSystem.Services
{
    public class ContentReaderService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ContentReaderService> _logger;

        public ContentReaderService(AppDbContext dbContext, ILogger<ContentReaderService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task SyncContentFolderAsync(string basePath, string userId)
        {
            if (!Directory.Exists(basePath))
            {
                _logger.LogWarning("Content base path does not exist: {BasePath}", basePath);
                return;
            }

            var directories = Directory.GetDirectories(basePath);
            int syncedCount = 0;

            foreach (var dir in directories)
            {
                var dirName = new DirectoryInfo(dir).Name;
                if (dirName.StartsWith("Day", StringComparison.OrdinalIgnoreCase))
                {
                    var numberPart = dirName.Substring(3).Trim('_', '-', ' ');
                    if (int.TryParse(numberPart, out int dayNumber))
                    {
                        // Check if we already have this day in the DB for this user
                        if (_dbContext.ScheduledPosts.Any(p => p.DayNumber == dayNumber && p.UserId == userId))
                        {
                            continue; // Skip already synced
                        }

                        var textFile = Path.Combine(dir, "post.txt");
                        if (!File.Exists(textFile))
                        {
                            _logger.LogWarning("post.txt not found in folder {Folder}", dir);
                            continue;
                        }

                        var textContent = await File.ReadAllTextAsync(textFile);
                        
                        // Find first image
                        var imagePath = Directory.GetFiles(dir, "*.*").FirstOrDefault(f => 
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

                        var post = new ScheduledPost
                        {
                            DayNumber = dayNumber,
                            ContentText = textContent,
                            ImagePath = imagePath,
                            Status = "Pending",
                            UserId = userId,
                            // ScheduledDate = Today + (DayNumber - 1)
                            ScheduledDate = DateTime.Today.AddDays(dayNumber - 1)
                        };

                        _dbContext.ScheduledPosts.Add(post);
                        syncedCount++;
                        _logger.LogInformation("Synced Day {DayNumber}", dayNumber);
                    }
                }
            }

            if (syncedCount > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully synced {Count} new posts.", syncedCount);
            }
        }
    }
}
