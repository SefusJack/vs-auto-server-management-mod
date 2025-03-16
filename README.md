# AutoServerManagement Mod for Vintage Story

A server-only mod that automates backups, manages backup retention, and ensures a forced `/save` if the server hasn’t recently saved when a backup runs.

## Features

- Automatically triggers a backup at a configurable interval.
- Uses the built-in `/genbackup` command.
- Keeps a limited number of recent backups while also ensuring at least one daily-, weekly-, and monthly-old backup is retained.
- Forces a `/save` if it has been over a specified threshold (e.g., 30 minutes) since the last save, ensuring the world is fully up-to-date before backing up.
- Provides a `/autoserver` command to start or stop auto-backups manually.

## Requirements

- Vintage Story server version **1.20+** (where `/genbackup` and `/save` commands are available).
- [Visual Studio](https://visualstudio.microsoft.com/) or another C#-capable IDE if you plan to modify or build from source.
- .NET runtime/SDK compatible with your Vintage Story modding environment (.NET 7.0).

## Installation

1. **Set** the `VINTAGE_STORY` environment variable. It should be equal to the location of your Vintage Story folder (default path is `C:\Users\user\AppData\Roaming\Vintagestory`)
2. **Build** the project in Visual Studio (or your chosen IDE). You should end up with a `.dll` containing your compiled mod code.
3. **Place** the built mod (the `.dll` or the folder) into your **`Mods`** folder in the Vintage Story server directory.
4. **Restart** your Vintage Story server.  
   - The mod will load automatically on the server side.

## Usage

Once installed and running:

- By default, auto-backups start **every 60 minutes** on server launch.
- You can control backups manually with `/autoserver` commands (server console or an admin player):
  - **`/autoserver start [minutes]`** – Start auto-backups at the specified interval (default 30 if not provided).
  - **`/autoserver stop`** – Stop auto-backups.

### Backup Retention

- Keeps the **3 newest backups** at all times.
- Ensures you also have at least one backup **1 day** old, **1 week** old, and **1 month** old – if available.
- If none exist for a threshold, the **oldest** backup is kept as a fallback.

### Forced Saves

- If the server hasn’t saved in **30 minutes** (configurable), it will force a `/save` before running `/genbackup`.
- Prevents partial or outdated data from being included in your backups.

## Configuration

You can adjust intervals and thresholds by changing the constants in the source code:

- **`StartAutoBackups(60)`** in `StartServerSide` sets the default backup interval on server start (in minutes).
- **`saveThresholdMinutes = 30;`** defines how long since the last save triggers a forced `/save`.

Feel free to modify these values as needed in the **`AutoServerManagementController.cs`** file.

## Contributing

1. **Fork** this repository or clone it locally.
2. **Create** a new branch for your feature or fix.
3. **Commit** and push your changes.
4. **Create** a pull request for review.