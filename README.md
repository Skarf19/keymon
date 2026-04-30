# KEYMON

A background app that watches how you type and shows your focus level through a pixel cat in the taskbar.

---

## Before You Start

You only need to install one thing:

### [Download .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
Pick **SDK (Windows x64)** and install it.

Check it worked by opening a terminal and typing:
```bash
dotnet --version
```
You should see a number starting with `10.`

---

## How to Run

```bash
# Step 1 — go into the project folder
cd keymon

# Step 2 — run it
dotnet run --project src/Keymon.Core.csproj

or

cd src
donet run
```

The first time you run it, it downloads some packages automatically. 

---

## What You'll See

- **A pixel cat window** pops up — this is the character that reacts to your focus
- **A small icon in the bottom-right taskbar** — that's the app running in the background

**Right-click the taskbar icon** to open the dashboard or close the app.

The cat does not react to the typing but moving.

---

## How It Works

1. The app runs in the background and watches your typing rhythm
2. Every minute it figures out your focus level
3. There are 5 states: `Idle → Distracted → Engaged → Focused → Deep Focus`
4. The cat's animation changes to match your state (not working)
5. The dashboard shows your stats in real time

---

## Troubleshooting

**`dotnet` command not found**
→ .NET 10 SDK isn't installed yet, or you need to restart your terminal after installing

---

## Team Rules

- Don't push directly to `main`
- Always use a feature branch (`feature/your-feature-name`)
- Open a pull request before merging
