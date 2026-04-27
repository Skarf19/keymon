# KEYMON

A background app that watches how you type and shows your focus level through a pixel cat in the taskbar.

> It tracks **how** you type — not **what** you type. Nothing is recorded or sent anywhere.

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
dotnet run --project src/core/Keymon.Core.csproj
```

The first time you run it, it downloads some packages automatically. Just wait about 30 seconds.

---

## What You'll See

- **A pixel cat window** pops up — this is the character that reacts to your focus
- **A small icon in the bottom-right taskbar** — that's the app running in the background

**Right-click the taskbar icon** to open the dashboard or close the app.

The cat won't do anything for the first 60 seconds — it's learning your typing pattern first. That's normal.

---

## How It Works

1. The app runs in the background and watches your typing rhythm
2. Every minute it figures out your focus level
3. There are 5 states: `Idle → Distracted → Engaged → Focused → Deep Focus`
4. The cat's animation changes to match your state
5. The dashboard shows your stats in real time

---

## Troubleshooting

**`dotnet` command not found**
→ .NET 10 SDK isn't installed yet, or you need to restart your terminal after installing

**Cat window doesn't appear**
→ Make sure the `bin/unity/` folder exists in the project — don't delete it

**Cat just sits there and doesn't change**
→ Wait 60 seconds for the first analysis to finish

**App won't start — says file is in use**
→ A previous version is still running. Right-click the taskbar icon and exit it first

---

## Team Rules

- Don't push directly to `main`
- Always use a feature branch (`feature/your-feature-name`)
- Open a pull request before merging
