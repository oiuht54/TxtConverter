# TxtConverter

[🇷🇺 Читать на русском](#-txtconverter-ru)

**TxtConverter** is a professional desktop utility designed to quickly and safely prepare project source code for analysis by Neural Networks (LLMs), archiving, or sharing in chats.

The application scans your project folder and creates an optimized single text file that is easy to feed into ChatGPT, Claude, DeepSeek, or Gemini.

**⚡ Now rebuilt with .NET 10 & WPF for maximum performance!**

<!-- Screenshots Side-by-Side -->
<p align="center">
  <img src="docs/screenshot_en.png" width="48%" alt="English Interface" />
  <img src="docs/screenshot_ru.png" width="48%" alt="Russian Interface" />
</p>

---

## 🚀 Why the New Version?
This project is a complete rewrite of the original Java version.
*   **Native Performance:** Built on .NET 10 and WPF (Windows Presentation Foundation).
*   **Single File EXE:** No installation required. No Java runtime needed. Just run and use.
*   **Modern UI:** Dark theme, flat design, responsive interface.
*   **Faster Scanning:** Optimized multithreaded file system enumeration.

---

## 🌍 Multilingual Support
The application fully supports **English** and **Russian** languages.
*   **First Run:** You will be prompted to select your preferred language.
*   **Settings:** You can change the language at any time using the Settings (⚙) menu.
*   **Persistence:** Your choice is saved automatically for future sessions.

---

## 🔥 Key Features

### 🚀 Smart Automation & UX
*   **Drag & Drop:** Simply drag your project folder into the application window to start.
*   **Auto-Detection:** The app automatically analyzes project files (e.g., `package.json`, `pom.xml`, `project.godot`) and selects the correct Preset for you.
*   **Smart Persistence:** The app remembers your last used folder, settings, and window position.

### 🧠 Optimization for LLMs (AI)
*   **Token Compression:**
    *   **Smart:** Removes excessive empty lines and normalizes line endings (`LF`).
    *   **Maximum:** Removes all comments and formatting, turning code into a flat list of commands to save maximum tokens.
*   **Smart Merging:** You can choose which files to include **fully** and which to keep as **stubs**.
    *   *Example:* If a file is found but not selected for merging, the report will contain: `(Stub)`. This gives the AI context about the file's existence without wasting tokens on its content.

### ⚡ Performance & Safety
*   **Turbo Scanning:** Optimized algorithm instantly skips massive ignored folders (like `node_modules`, `.git`, or `Library`), making scanning instant even for huge projects.
*   **Non-Destructive:** The app **never** modifies your source files. All results are saved in a separate `_ConvertedToTxt` folder inside your project.

### ⚙️ Flexibility
*   **Presets:** Ready-made settings for:
    *   **Unity Engine** / **Godot Engine**
    *   **Java (Maven/Gradle)**
    *   **Web (TypeScript/React)** / **Web (JavaScript/Legacy)**
    *   **Python**
    *   **C# (.NET / Visual Studio)**
*   **Structure Map:** Optional generation of a `_FileStructure.md` file (Tree or Flat list).

---

## 🚀 How to Use

1.  Download `TxtConverter.exe` from Releases.
2.  Run it (No installation needed).
3.  **Drag & Drop** your project folder into the window (or click "Select...").
4.  The app will try to **Auto-Detect** the preset. If needed, change it manually.
5.  Click **"Rescan"** (if not triggered automatically).
6.  (Optional) Click **"Select Files..."** to check only the scripts you need in full.
7.  Click the big blue button **"START CONVERSION"**.
8.  Once done, check the created `_ConvertedToTxt` folder.

---

## 🛠️ Build from Source

The project is built on **.NET 10** (WPF).

### Requirements
*   .NET 10 SDK
*   Visual Studio 2022 (or VS Code)

### Build Command (Single File)

```bash
dotnet publish -c Release
```
The ready-to-use application will be in: `bin/Release/net10.0-windows/win-x64/publish/`

---

<br>
<br>

# 🇷🇺 TxtConverter (RU)

**TxtConverter** — это профессиональная десктопная утилита для быстрой и безопасной подготовки исходного кода проектов к анализу нейросетями (LLM), архивации или отправке в чаты.

**⚡ Полностью переписанная версия на .NET 10 (WPF)!**

---

## 🚀 В чем отличия от старой версии?
*   **Скорость:** Нативный код .NET работает значительно быстрее и отзывчивее.
*   **Портативность:** Один `exe` файл (60 МБ), который работает везде. Не нужно устанавливать Java.
*   **Дизайн:** Современная темная тема и удобный интерфейс.

---

## 🌍 Мультиязычность
Приложение полностью поддерживает **Русский** и **Английский** языки.
*   **Память:** Ваш выбор языка и настроек сохраняется автоматически.

---

## 🔥 Ключевые возможности

### 🚀 Автоматизация
*   **Drag & Drop:** Просто перетащите папку проекта в окно программы.
*   **Авто-определение:** Приложение само находит ключевые файлы (`project.godot`, `pom.xml`, `package.json`, `.sln`) и выставляет нужный пресет.
*   **Сохранение настроек:** Программа запоминает последнюю папку, пресет и галочки настроек.

### 🧠 Оптимизация для LLM (ИИ)
*   **Сжатие токенов:**
    *   **Умное:** Удаляет лишние пустые строки, нормализует переносы строк (`LF`).
    *   **Максимум:** Удаляет комментарии и форматирование, максимально экономя контекст нейросети.
*   **Умное слияние:** Выбор файлов, которые нужны **полностью**, и файлов, которые нужны только как **заглушки** (для контекста).

### ⚙️ Пресеты
*   **GameDev:** Unity, Godot.
*   **Web:** TypeScript (Modern), JavaScript (Classic).
*   **Backend:** Java, Python, C# (.NET).

---

## 🚀 Как использовать

1.  Скачайте `TxtConverter.exe` из раздела Releases.
2.  Запустите (Установка не требуется).
3.  **Перетащите папку** проекта в окно (или нажмите "Выбрать...").
4.  Приложение автоматически определит тип проекта (Пресет).
5.  Нажмите **"Начать конвертацию"**.
6.  Заберите готовый файл в папке `_ConvertedToTxt`.

---

*TxtConverter — Making AI coding easier.*
