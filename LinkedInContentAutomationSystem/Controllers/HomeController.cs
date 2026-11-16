using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LinkedInContentAutomationSystem.Models;
using LinkedInContentAutomationSystem.Data;
using LinkedInContentAutomationSystem.Services;
using LinkedInContentAutomationSystem.PlaywrightAutomation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace LinkedInContentAutomationSystem.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _dbContext;
    private readonly ContentReaderService _contentReader;
    private readonly LinkedInPublisher _publisher;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(
        AppDbContext dbContext, 
        ContentReaderService contentReader, 
        LinkedInPublisher publisher, 
        IWebHostEnvironment env, 
        IConfiguration configuration,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _contentReader = contentReader;
        _publisher = publisher;
        _env = env;
        _configuration = configuration;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);
        var posts = await _dbContext.ScheduledPosts
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.DayNumber)
            .ToListAsync();
            
        return View(posts);
    }

    [HttpGet]
    public async Task<IActionResult> GetPosts()
    {
        var userId = _userManager.GetUserId(User);
        var posts = await _dbContext.ScheduledPosts
            .Where(p => p.UserId == userId)
            .ToListAsync();
            
        var events = posts.Select(p => new
        {
            id = p.Id,
            title = $"Day {p.DayNumber}",
            start = p.ScheduledDate.ToString("yyyy-MM-dd"),
            backgroundColor = p.Status == "Posted" ? "#198754" : (p.Status == "Failed" ? "#dc3545" : "#0d6efd"),
            borderColor = p.Status == "Posted" ? "#198754" : (p.Status == "Failed" ? "#dc3545" : "#0d6efd"),
            textColor = "#fff",
            extendedProps = new { status = p.Status, retryCount = p.RetryCount }
        });
        
        return Json(events);
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePostDate(int id, string newDate)
    {
        var userId = _userManager.GetUserId(User);
        var post = await _dbContext.ScheduledPosts.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        
        if (post == null) return NotFound();

        if (DateTime.TryParse(newDate, out DateTime parsedDate))
        {
            post.ScheduledDate = parsedDate;
            await _dbContext.SaveChangesAsync();
            return Ok();
        }
        
        return BadRequest("Invalid date format");
    }

    [HttpPost]
    public async Task<IActionResult> SyncContent()
    {
        // For SaaS, this would typically unpack a ZIP file upload. 
        // For now, it syncs the local directory to the current logged-in user.
        var contentPath = _configuration["ContentFolderPath"];
        if (string.IsNullOrEmpty(contentPath))
        {
            contentPath = Path.Combine(_env.ContentRootPath, "Content");
        }
        
        // We need to pass the UserId to ContentReaderService so it assigns it to the new posts
        var userId = _userManager.GetUserId(User);
        await _contentReader.SyncContentFolderAsync(contentPath, userId);
        
        TempData["SuccessMessage"] = $"Content folder synced successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> PostNow(int id)
    {
        var userId = _userManager.GetUserId(User);
        var post = await _dbContext.ScheduledPosts.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (post == null) return NotFound();

        try
        {
            await _publisher.PublishPostAsync(post.ContentText, post.ImagePath, userId);
            post.Status = "Posted";
            post.PostedAt = DateTime.Now;
            post.ErrorMessage = null;
            TempData["SuccessMessage"] = $"Post for Day {post.DayNumber} published successfully!";
        }
        catch (Exception ex)
        {
            post.RetryCount++;
            post.ErrorMessage = ex.Message;
            if (post.RetryCount >= 3)
            {
                post.Status = "Failed";
            }
            TempData["ErrorMessage"] = $"Failed to publish: {ex.Message}";
        }

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ResetStatus(int id)
    {
        var userId = _userManager.GetUserId(User);
        var post = await _dbContext.ScheduledPosts.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (post == null) return NotFound();

        post.Status = "Pending";
        post.RetryCount = 0;
        post.ErrorMessage = null;
        post.PostedAt = null;

        await _dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Status for Day {post.DayNumber} reset to Pending.";
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
