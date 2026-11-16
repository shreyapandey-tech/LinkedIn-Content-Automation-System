using Microsoft.Playwright;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LinkedInContentAutomationSystem.PlaywrightAutomation
{
    public class LinkedInPublisher
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LinkedInPublisher> _logger;

        public LinkedInPublisher(IConfiguration configuration, ILogger<LinkedInPublisher> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task PublishPostAsync(string textContent, string? imagePath, string userId)
        {
            var headless = _configuration.GetValue<bool>("Playwright:Headless");
            var dryRun = _configuration.GetValue<bool>("Playwright:DryRun");
            
            // Isolate Playwright profiles per user!
            var userDataDir = Path.Combine(Directory.GetCurrentDirectory(), "PlaywrightProfiles", userId);

            _logger.LogInformation("Starting Playwright to publish post. Headless mode: {Headless}, DryRun: {DryRun}, User: {UserId}", headless, dryRun, userId);

            if (dryRun)
            {
                _logger.LogInformation("Dry run enabled. Skipping actual post to LinkedIn.");
                await Task.Delay(1000); // Simulate work
                return;
            }

            using var playwright = await Playwright.CreateAsync();
            var browserContext = await playwright.Chromium.LaunchPersistentContextAsync(userDataDir, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = headless,
                Args = new[] { "--disable-blink-features=AutomationControlled", "--start-maximized" },
                ViewportSize = ViewportSize.NoViewport
            });

            var page = browserContext.Pages.Count > 0 ? browserContext.Pages[0] : await browserContext.NewPageAsync();

            try
            {
                await page.GotoAsync("https://www.linkedin.com/feed/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

                // We use a try-catch to determine if we are fully logged in. 
                // If we don't see the 'Start a post' button within 15 seconds, we assume LinkedIn 
                // threw a login screen, security checkpoint, or captcha at us.
                var startPostButton = page.Locator("button:has-text('Start a post'), [aria-label='Start a post'], .share-box-feed-entry__trigger, span:has-text('Start a post')").First;

                _logger.LogInformation("Checking for 'Start a post' button. Waiting up to 5 minutes for any manual login/verification...");

                if (headless)
                {
                    _logger.LogWarning("Running in headless mode. If LinkedIn requires a security checkpoint, this will timeout.");
                }

                bool isReady = false;
                for (int i = 0; i < 150; i++) // 150 * 2 seconds = 5 minutes
                {
                    try
                    {
                        if (await startPostButton.IsVisibleAsync())
                        {
                            isReady = true;
                            break;
                        }

                        // If LinkedIn redirected to the profile page after login, force it back to the feed
                        if (page.Url.Contains("linkedin.com/in/"))
                        {
                            _logger.LogInformation("Redirected to profile page. Navigating back to feed...");
                            await page.GotoAsync("https://www.linkedin.com/feed/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                        }
                    }
                    catch (PlaywrightException ex)
                    {
                        // If the user manually closes the browser, exit immediately
                        if (ex.Message.Contains("closed")) throw;
                    }

                    await Task.Delay(2000);
                }

                if (!isReady)
                {
                    if (headless) throw new Exception("Timed out waiting for 'Start a post' button. You might be blocked by a login or captcha. Run with Headless: false once.");
                    throw new Exception("Timed out waiting 5 minutes for the 'Start a post' button. Ensure you are fully logged in and on the feed.");
                }

                _logger.LogInformation("Successfully located 'Start a post' button. Waiting a moment for event listeners...");
                await Task.Delay(2000); // Give LinkedIn's SPA time to attach click handlers

                // Click 'Start a post'
                await startPostButton.ClickAsync();

                var initialDialog = page.Locator("div[role='dialog']").Last;
                try
                {
                    await initialDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                }
                catch (System.TimeoutException)
                {
                    _logger.LogWarning("Editor modal did not appear. The click might have been missed. Trying again...");
                    await startPostButton.ClickAsync(new() { Force = true });
                    await initialDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                }
                
                // Upload image FIRST if provided. This prevents LinkedIn from clearing typed text when the photo modal opens.
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    _logger.LogInformation("Uploading image: {ImagePath}", imagePath);
                    
                    try
                    {
                        var mediaButton = initialDialog.Locator("button[aria-label*='media' i], button[aria-label*='photo' i], button[aria-label*='image' i], button[aria-tooltip*='media' i]").First;
                        
                        var fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
                        {
                            await mediaButton.ClickAsync();
                        }, new() { Timeout = 5000 });
                        
                        await fileChooser.SetFilesAsync(imagePath);
                    }
                    catch (System.TimeoutException)
                    {
                        _logger.LogWarning("Native file picker did not open. Falling back to hidden input...");
                        var fileInput = page.Locator("input[type='file']").First;
                        await fileInput.SetInputFilesAsync(imagePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to click media button. Trying hidden input fallback...");
                        var fileInput = page.Locator("input[type='file']").First;
                        await fileInput.SetInputFilesAsync(imagePath);
                    }
                    
                    await Task.Delay(3000); // Give LinkedIn time to process the preview

                    // The photo modal usually has a 'Next' or 'Done' button. Ensure it's inside the topmost active dialog.
                    var photoDialog = page.Locator("div[role='dialog']").Last;
                    // Use exact text match or primary button class to avoid clicking random buttons
                    var nextButton = photoDialog.Locator("button.artdeco-button--primary:has-text('Next'), button.artdeco-button--primary:has-text('Done'), button:text-is('Next'), button:text-is('Done')").First;
                    if (await nextButton.IsVisibleAsync())
                    {
                        await nextButton.ClickAsync();
                        await Task.Delay(2000); // Wait for the modal to transition back to main editor
                    }
                }

                // NOW type the content into the main editor
                var activeDialog = page.Locator("div[role='dialog']").Last;
                var editorLocator = activeDialog.Locator("div[role='textbox'], div.ql-editor, .editor-content, div[aria-label*='Text editor']").First;
                
                await editorLocator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                await editorLocator.ClickAsync();
                
                // Using FillAsync is much faster and more reliable than Keyboard.TypeAsync
                await editorLocator.FillAsync(textContent);
                await Task.Delay(1000); // Wait for text to settle in the DOM

                // Post it. Ensure we click the Post button inside the dialog!
                var postDialog = page.Locator("div[role='dialog']").Last;
                // Use strict exact text matching and primary button class to prevent clicking the "Post settings" button
                var postButton = postDialog.Locator("button.artdeco-button--primary:has-text('Post'):not([disabled]), button:text-is('Post'):not([disabled])").First;
                await postButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                
                await Task.Delay(1000); // Wait for button to be fully clickable
                await postButton.ClickAsync();

                try
                {
                    // Wait for the modal to close, which indicates the post was submitted
                    await postDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15000 });
                }
                catch (System.TimeoutException)
                {
                    _logger.LogWarning("Post dialog did not close. The click might have been missed. Trying again...");
                    await postButton.ClickAsync(new() { Force = true });
                    await postDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30000 });
                }

                _logger.LogInformation("Post dialog closed. Waiting 15 seconds for LinkedIn to process the upload...");
                // CRITICAL: We must wait long enough for the background HTTP upload request to finish. 
                // If Playwright closes the browser immediately, the post upload is aborted mid-flight.
                await Task.Delay(15000); 
                _logger.LogInformation("Post successfully published.");
            }
            catch (Exception ex)
            {
                var screenshotPath = $"error_{DateTime.Now:yyyyMMddHHmmss}.png";
                await page.ScreenshotAsync(new() { Path = screenshotPath });
                _logger.LogError(ex, "Failed to publish post to LinkedIn. Screenshot saved to {ScreenshotPath}", screenshotPath);
                throw;
            }
            finally
            {
                await browserContext.CloseAsync();
            }
        }
    }
}
