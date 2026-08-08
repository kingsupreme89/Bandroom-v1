"""Generate a dead-simple PDF guide: How to Launch Bandroom on Your Mac"""
from reportlab.lib.pagesizes import LETTER
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.colors import HexColor
from reportlab.lib.units import inch
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak

OUTPUT = r"C:\Bandroom\Bandroom_Mac_Launch_Guide.pdf"

doc = SimpleDocTemplate(OUTPUT, pagesize=LETTER,
                        leftMargin=0.75*inch, rightMargin=0.75*inch,
                        topMargin=0.5*inch, bottomMargin=0.5*inch,
                        title="Bandroom Mac - How to Launch")

styles = getSampleStyleSheet()

# Custom styles — big, friendly, lots of spacing
title_style = ParagraphStyle('BigTitle', parent=styles['Title'],
    fontSize=28, spaceAfter=6, textColor=HexColor('#6366f1'), alignment=1)
h1 = ParagraphStyle('StepH1', parent=styles['Heading1'],
    fontSize=22, spaceBefore=18, spaceAfter=8, textColor=HexColor('#1e1b4b'))
h2 = ParagraphStyle('StepH2', parent=styles['Heading2'],
    fontSize=18, spaceBefore=12, spaceAfter=6, textColor=HexColor('#312e81'))
body = ParagraphStyle('BigBody', parent=styles['Normal'],
    fontSize=14, leading=22, spaceAfter=8, textColor=HexColor('#1f2937'))
code_style = ParagraphStyle('CodeBlock', parent=styles['Code'],
    fontSize=13, leading=18, backColor=HexColor('#f3f4f6'),
    borderColor=HexColor('#d1d5db'), borderWidth=1, borderPadding=8,
    spaceBefore=4, spaceAfter=10, fontName='Courier')
tip = ParagraphStyle('Tip', parent=styles['Normal'],
    fontSize=12, leading=18, spaceAfter=6, textColor=HexColor('#6b7280'),
    leftIndent=12, borderColor=HexColor('#e5e7eb'), borderWidth=0.5,
    borderPadding=6, backColor=HexColor('#f9fafb'))
emoji = ParagraphStyle('Emoji', parent=styles['Normal'],
    fontSize=36, alignment=1, spaceBefore=4, spaceAfter=4)

story = []

# ---- COVER ----
story.append(Paragraph("🏈", emoji))
story.append(Paragraph("Bandroom for Mac", title_style))
story.append(Paragraph("How to Launch & Test<br/>Step-by-Step (Even Your Kid Could Do It)", ParagraphStyle('Sub', parent=styles['Normal'], fontSize=16, alignment=1, spaceAfter=24, textColor=HexColor('#6b7280'))))
story.append(Spacer(1, 0.3*inch))

# ---- STEP 0: BEFORE YOU START ----
story.append(Paragraph("🧰 BEFORE YOU START (Check These First)", h1))
story.append(Paragraph("1. You need a <b>Mac</b> (laptop or desktop)", body))
story.append(Paragraph("2. Make sure you are connected to Wi-Fi", body))
story.append(Paragraph("3. Open the app called <b>Terminal</b> (Finder → Applications → Utilities → Terminal)", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;Tip: Press ⌘+Space, type \"Terminal\", press Enter", tip))
story.append(Paragraph("4. Install .NET 10 SDK if you haven't:", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;a) Open Safari, go to: <font color='#2563eb'>https://dotnet.microsoft.com/download/dotnet/10.0</font>", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;b) Click the <b>macOS ARM64</b> (M1/M2/M3/M4) or <b>macOS x64</b> (Intel) download", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;c) Open the downloaded .pkg file and click Next until done", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;d) Close and re-open Terminal", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;e) Type: <font face='Courier'>dotnet --version</font> → should print \"10.0.something\"", body))
story.append(Spacer(1, 0.2*inch))

# ---- STEP 1: COPY FILES ----
story.append(Paragraph("📂 STEP 1 — Copy the Bandroom Folder to Your Mac", h1))
story.append(Paragraph("You have the entire folder on your PC at:", body))
story.append(Paragraph("<font face='Courier'>C:\\Bandroom</font>", code_style))
story.append(Paragraph("Copy this <b>entire folder</b> to your Mac. Do ONE of these:", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>Option A (USB stick):</b> Copy the folder onto a USB stick, plug it into your Mac, drag the folder to your Mac's Desktop.", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>Option B (Network share):</b> Share the folder from Windows, connect to it from Finder → Go → Connect to Server.", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>Option C (Zip):</b> Right-click the Bandroom folder → Send to → Compressed folder. Email it to yourself or use Dropbox/Google Drive.", body))
story.append(Paragraph("Once it's on your Mac, put it in your <b>home folder</b> so the path is: <font face='Courier'>~/Bandroom</font>", body))
story.append(Paragraph("(That means: Macintosh HD → Users → your-username → Bandroom)", body))
story.append(Spacer(1, 0.2*inch))

# ---- STEP 2: BUILD ----
story.append(Paragraph("🔨 STEP 2 — Build the App (Tell the Computer to Make It)", h1))
story.append(Paragraph("In Terminal, type these commands ONE AT A TIME, pressing Enter after each:", body))
story.append(Paragraph("cd ~/Bandroom", code_style))
story.append(Paragraph("dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj", code_style))
story.append(Paragraph("You will see a bunch of text scroll by. Look for these magic words at the end:", body))
story.append(Paragraph("<font face='Courier'><b>Build succeeded.</b></font>", code_style))
story.append(Paragraph("If you see \"Build FAILED\" instead — see Troubleshooting at the end.", tip))
story.append(Spacer(1, 0.2*inch))

# ---- STEP 3: RUN ----
story.append(Paragraph("🚀 STEP 3 — Launch It!", h1))
story.append(Paragraph("In Terminal, type:", body))
story.append(Paragraph("dotnet run --project src/Bandroom.Mac/Bandroom.Mac.csproj", code_style))
story.append(Paragraph("Two things happen:", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;✅ A small gray window opens with \"Bandroom\" text", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;✅ Your web browser opens automatically to http://localhost:18765", body))
story.append(Paragraph("That's it! You're looking at Bandroom! 🎉", body))
story.append(Spacer(1, 0.2*inch))

# ---- STEP 4: TEST ----
story.append(Paragraph("🧪 STEP 4 — What to Click & Test", h1))
story.append(Paragraph("Now that it's running, try these things:", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>1. Pick a team:</b> Click the team logo in the top-left corner. Search for \"Alabama\" or \"Georgia\". Click it. The whole app changes color to that team!", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>2. Browse the Event Board:</b> Look at the left panel. Click \"Downs\", \"Scoring\", \"Turnovers\" tabs. Each one shows events you can assign songs to.", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>3. Check the Marketplace:</b> Click \"Marketplace\" in the top bar. You should see song/background cards in a grid. Try clicking \"Preview\" on one.", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>4. My Downloads:</b> Click the \"My Downloads\" tab in Marketplace. Shows what you've already downloaded.", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>5. Profile:</b> Click the person icon top-right. You can sign in with Google, set your favorite team, etc.", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>6. UI Bot Check:</b> Press F12 (or ⌘+Option+I) to open Developer Tools → Console tab. Look for colored lines with ✅ and ❌ — that's the automatic bug scanner reporting!", body))
story.append(Paragraph("&nbsp;&nbsp;&nbsp;&nbsp;<b>7. Play a sound:</b> Click \"Effects Test\" (bottom-left gear icon). It plays the touchdown sound!", body))
story.append(Spacer(1, 0.2*inch))

# ---- TROUBLESHOOTING ----
story.append(PageBreak())
story.append(Paragraph("🆘 TROUBLESHOOTING (If Something Goes Wrong)", h1))
story.append(Spacer(1, 0.1*inch))

story.append(Paragraph("❌ \"dotnet: command not found\"", h2))
story.append(Paragraph("→ .NET 10 SDK is not installed. Go back to Step 0 part 4.", body))

story.append(Paragraph("❌ \"Build FAILED\"", h2))
story.append(Paragraph("→ Copy the error message and ask Claude/ChatGPT what's wrong.", body))
story.append(Paragraph("→ Common fix: make sure the WHOLE Bandroom folder was copied, not just some files.", body))

story.append(Paragraph("❌ Browser doesn't open / \"Unable to connect\"", h2))
story.append(Paragraph("→ In your browser address bar, manually type: <font face='Courier'>http://localhost:18765/index.html</font>", body))
story.append(Paragraph("→ If that doesn't work: close Terminal, open a new Terminal, repeat Steps 2-3.", body))

story.append(Paragraph("❌ Window says \"wwwroot not found\"", h2))
story.append(Paragraph("→ The Bandroom folder may not be complete. Make sure the <font face='Courier'>wwwroot</font> subfolder exists inside <font face='Courier'>~/Bandroom</font>.", body))
story.append(Paragraph("→ Check by typing in Terminal: <font face='Courier'>ls ~/Bandroom/wwwroot</font> (should show index.html, app.js, style.css, ui-bot.js)", body))

story.append(Paragraph("❌ No sounds play when clicking Effects Test", h2))
story.append(Paragraph("→ Make sure the <font face='Courier'>Songs/Default</font> folder exists and has .mp3 files in it.", body))
story.append(Paragraph("→ Check: <font face='Courier'>ls ~/Bandroom/Songs/Default</font> (should show many .mp3 files)", body))

story.append(Paragraph("❌ \"Permission denied\" or \"Screen Recording\" popup", h2))
story.append(Paragraph("→ The game-watching feature (OCR) needs Screen Recording permission. You can skip this for testing — the app works fine without it. Go to System Preferences → Privacy → Screen Recording and allow Terminal if you want OCR.", body))

story.append(Spacer(1, 0.3*inch))

# ---- QUICK REFERENCE CARD ----
story.append(Paragraph("📋 QUICK REFERENCE CARD", h1))
data = [
    ["What to type", "What it does"],
    ["cd ~/Bandroom", "Go to the Bandroom folder"],
    ["dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj", "Build (compile) the app"],
    ["dotnet run --project src/Bandroom.Mac/Bandroom.Mac.csproj", "Launch the app!"],
    ["Ctrl+C (in Terminal)", "Stop the running app"],
    ["http://localhost:18765", "Open app in browser manually"],
]
t = Table(data, colWidths=[3.5*inch, 3.5*inch])
t.setStyle(TableStyle([
    ('BACKGROUND', (0, 0), (-1, 0), HexColor('#6366f1')),
    ('TEXTCOLOR', (0, 0), (-1, 0), HexColor('#ffffff')),
    ('FONTSIZE', (0, 0), (-1, -1), 12),
    ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
    ('FONTNAME', (0, 1), (0, -1), 'Courier'),
    ('FONTNAME', (1, 1), (-1, -1), 'Helvetica'),
    ('ALIGN', (0, 0), (-1, -1), 'LEFT'),
    ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
    ('GRID', (0, 0), (-1, -1), 0.75, HexColor('#d1d5db')),
    ('ROWBACKGROUNDS', (0, 1), (-1, -1), [HexColor('#ffffff'), HexColor('#f9fafb')]),
    ('TOPPADDING', (0, 0), (-1, -1), 6),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
    ('LEFTPADDING', (0, 0), (-1, -1), 10),
]))
story.append(t)

story.append(Spacer(1, 0.4*inch))
story.append(Paragraph("Built with ❤️ August 8, 2026 | Bandroom v1.0 | 17 evaluators · 0 build errors", ParagraphStyle('Footer', parent=styles['Normal'], fontSize=10, alignment=1, textColor=HexColor('#9ca3af'))))

doc.build(story)
print(f"PDF created: {OUTPUT}")