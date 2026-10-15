# MDTab-Helper

Aplicație avansată C# WPF pentru afișarea și managementul multiplelor imagini ca overlay, tabletă virtuală in-game (Chromium), automatizare comenzi peste jocuri și sistem integrat de **Auto-Update**. Funcționează cel mai bine în modul **Borderless Windowed**.

## ✨ Funcționalități

- 📱 **Tabletă In-Game** - Browser Chromium integrat cu lockscreen și fundaluri personalizate
- ⚡ **Auto-Update Integrat** - Verificare automată a lansărilor pe GitHub Releases și actualizare silențioasă printr-un singur click
- 🖼️ **Multiple imagini** - Administrează mai multe overlay-uri simultan
- ⌨️ **Automatizare Keybinds** - Execută comenzi de chat automate cu delay configurabil
- 🚗 **Copilot Assist** - Menținere automată a tastelor de condus/mers (`RightCtrl + /`)
- 💾 **Persistență** - Salvează și reîncarcă configurații complete (poziții, dimensiuni, setări)
- 🎮 **Borderless Windowed** - Funcționează perfect peste jocuri în modul borderless
- 🔓 **Click-through** - Poți juca "prin" imagini când sunt locked
- 🖱️ **Controale intuitive** - Drag, resize cu mouse wheel, opacity slider
- 📂 **Import/Export** - Salvează configurații în fișiere JSON

## 🚀 Cum se folosește

### 1. Instalare

Necesită [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Build și run
dotnet run

# Sau doar build
dotnet build
```

### 2. Lansează aplicația

```bash
dotnet run
```

## 🎮 Pentru GTA V / alte jocuri

1. **Setează jocul în Borderless Windowed:**
   - GTA V: Settings → Graphics → Screen Type → **Borderless Window**

2. **Adaugă imagini în MDTab-Helper:**
   - Click "+ Add Image" și selectează imaginile dorite

3. **Activează overlay-urile:**
   - Click "Show Overlays"

4. **Poziționează:**
   - Drag pentru mutare
   - Mouse wheel pentru resize
   - Ajustează opacity după preferințe

5. **Lock pentru gaming:**
   - Click 🔓 pentru a bloca imaginea (devine click-through)

## 🎯 Controale în Overlay

| Acțiune | Descriere |
|---------|-----------|
| **Drag** | Mută overlay-ul |
| **Mouse Wheel** | Resize imagine |
| **🔓/🔒** | Toggle locked mode (click-through) |
| **Slider** | Opacity 10% - 100% |
| **✕** | Închide overlay-ul |

## 🎯 Controale în Manager

| Acțiune | Descriere |
|---------|-----------|
| **+ Add Image** | Adaugă imagini noi |
| **Show/Hide Overlays** | Toggle vizibilitate toate |
| **💾 Save Config** | Salvează în AppData |
| **📂 Load Config** | Încarcă din fișier |
| **Export** | Exportă config ca JSON |

## ⌨️ Scurtături Implicite

| Acțiune | Tastă |
|---------|-------|
| **Oprire Script Chat** | `Ctrl + Shift + Backspace` |
| **Copilot (Auto-Hold)** | `RightCtrl + /` |
| **Toggle Overlay-uri** | ``RightCtrl + ` `` |
| **Toggle Fereastră Target** | `RightCtrl + -` |
| **Toggle Tabletă** | `F6` |

## 📁 Structura Configurație

```json
{
  "images": [
    {
      "id": "abc123",
      "filePath": "C:/path/to/image.png",
      "name": "image.png",
      "x": 100,
      "y": 150,
      "width": 400,
      "height": 300,
      "opacity": 0.8,
      "isVisible": true,
      "isLocked": false,
      "maintainAspectRatio": true
    }
  ]
}
```

## 📝 Note

- **Borderless Windowed**: ✅ Funcționează perfect
- **Exclusive Fullscreen**: ❌ Nu este suportat (limitare tehnică Windows/DirectX)
- Configurația se salvează automat în `%AppData%/MDTab-Helper/config.json`

## 🛠️ Tehnologii

- C# 12
- WPF (Windows Presentation Foundation)
- .NET 8
- Windows API (pentru overlay always-on-top)

## 🏗️ Build

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Single-file executable
build_exe.bat
```

---
Creat pentru overlay avansat în jocuri și management tabletă in-game.