# LinkedIn Content Automation System

A production-ready ASP.NET Core application that automatically schedules and posts LinkedIn content using local files.

## Tech Stack
* **Backend:** ASP.NET Core 8 MVC
* **Database:** SQL Server via Entity Framework Core
* **Scheduler:** Hangfire (SQL Server storage)
* **Automation:** Microsoft Playwright for .NET
* **Logging:** Serilog (File-based)
* **UI:** Bootstrap 5

## Setup Instructions

### 1. Prerequisites
- .NET 8 SDK
- SQL Server LocalDB (or standard SQL Server). The default connection string in `appsettings.json` points to LocalDB `(localdb)\mssqllocaldb`.

### 2. Installation
1. Restore packages and build the solution:
   ```bash
   dotnet build
   ```
2. Install Playwright browsers (if not already installed during setup):
   ```bash
   powershell -ExecutionPolicy Bypass -File bin\Debug\net8.0\playwright.ps1 install
   ```
   *(Note: The path might be `net8.0` or `net10.0` depending on the build output directory)*
3. Apply Database Migrations:
   ```bash
   dotnet ef database update
   ```

### 3. Usage
1. Place your content in the `Content` folder at the root of the project.
   - Example Structure:
     ```
     /Content
       /Day1
         post.txt
         image.png
       /Day2
         post.txt
         image.jpg
     ```
2. Run the application:
   ```bash
   dotnet run
   ```
3. Open the dashboard at `https://localhost:<port>/`
4. Click **Sync Content Folder** to read the posts from the `Content` folder into the database.

### 4. LinkedIn First-time Login (IMPORTANT)
To avoid logging in every time, the system uses a persistent Playwright browser profile.
1. Ensure `Headless: false` in `appsettings.json`.
2. Run the application, sync content, and click **Post Now** on the first pending post.
3. A visible Chromium browser will open and navigate to LinkedIn.
4. The system will detect you are not logged in and give you 2 minutes to manually log in.
5. Log in manually. Your session cookies will be saved in the `PlaywrightProfile` folder.
6. Once logged in, the post will continue.
7. Subsequent posts will use the saved session, and you can switch `Headless` back to `true` in `appsettings.json` for invisible background posting.

## Features
- **Dashboard UI**: Monitor and trigger posts manually.
- **Hangfire Scheduler**: Automatically posts daily at the configured time (`PostingTime` in `appsettings.json`).
- **Retry Mechanism**: Automatically retries failed posts up to 3 times.
- **File-based Logging**: Check `Logs/log-*.txt` for detailed execution logs.
