# Contributing

Thanks for improving SanJing.

## Local build

Requirements:

- Windows 10 or 11
- .NET 8 SDK

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

## Pull requests

- Keep changes focused and describe the behavior being changed.
- Do not commit personal folders, shortcuts, screenshots, build output or logs.
- Verify `dotnet build -c Release` before opening a pull request.
- For UI changes, include a screenshot with no personal or sensitive content.
