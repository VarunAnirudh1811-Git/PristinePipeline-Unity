# Pristine Pipeline ![Version](https://img.shields.io/github/v/release/VarunAnirudh1811-Git/PristinePipeline-Unity?label=Version&color=blue)

![Unity](https://img.shields.io/badge/Unity-6000.3%2B-black?logo=unity&logoColor=white) ![License](https://img.shields.io/badge/License-MIT-green.svg)

---

## What is Pristine Pipeline?

Pristine Pipeline is a Unity Editor extension (a tool that lives inside the Unity Editor, not in your game itself) with three built-in tools:

- **Folder Generator** — Instantly create a full folder structure from a saved template
- **Asset Organizer** — Automatically sort imported assets into the right folders
- **FBX Importer** — Apply consistent import settings to every FBX model you bring in

All three tools are accessible from a single window inside the Unity Editor.

---

## Features

### 📁 Folder Generator

- Create reusable folder structure templates and apply them to any project with one click
- Preview the folder tree before generating
- Export and import templates as JSON files to share with your team
- Add `.keep` files to empty folders so they're tracked by Git

### 🗂️ Asset Organizer

- Define rules that map file types (e.g. `.png`, `.wav`) to destination folders
- Optionally use filename patterns (e.g. `T_*` for textures) for fine-grained control
- Run a full organize pass manually, or let it run automatically on import
- Import and export profiles as JSON

### 🔧 FBX Importer

- Set up import presets (scale, normals, tangents, lightmap UVs, etc.) per file name pattern
- Enforce or warn on naming conventions (e.g. `SM_`, `SK_`, `P_`)
- Automatically create materials and assign textures after import
- Optionally generate prefabs on import with lightmap static support
- Reprocess all FBX files in your project in one click
- Import and export profiles as JSON

---

## Installation

> **Important:** Pristine Pipeline must be installed through the Unity Package Manager using one of the methods below. If installed incorrectly, the built-in default profiles and templates that come with the package will not be accessible inside the tool.

> **Requires:** Unity 6000.3 or later

---

### With Git installed

> [Git](https://git-scm.com/) must be installed on your machine and available in your system PATH for these methods.

#### Option 1 — Install via UPM (Recommended)

1. Open your Unity project
2. Go to **Window → Package Manager**
3. Click the **+** button in the top-left corner
4. Select **Add package from git URL...**
5. Paste the following URL and click **Add**:

```
https://github.com/VarunAnirudh1811-Git/PristinePipeline-Unity.git
```

#### Option 2 — Install via `manifest.json`

1. Close Unity
2. Open `<YourProject>/Packages/manifest.json` in a text editor
3. Add the following line inside the `"dependencies"` block:

```json
"com.glyphlabs.pristinepipeline": "https://github.com/VarunAnirudh1811-Git/PristinePipeline-Unity.git"
```

4. Save the file and reopen Unity — it will install the package automatically

#### Option 3 — Install a Specific Version

To lock to a specific release tag, append it to the URL with `#`:

```
https://github.com/VarunAnirudh1811-Git/PristinePipeline-Unity.git#v1.1.0
```

---

### Without Git installed

If you don't have Git installed, you can download the package manually and add it as a local package:

1. Go to the [Releases](https://github.com/VarunAnirudh1811-Git/PristinePipeline-Unity/releases) page on GitHub
2. Download the latest `.zip` file and extract it to a location **outside** your Unity project folder
3. Open your Unity project
4. Go to **Window → Package Manager**
5. Click the **+** button in the top-left corner
6. Select **Add package from disk...**
7. Navigate to the extracted folder and select the `package.json` file

> **Note:** When using a local package, you will need to repeat this process manually each time you want to update to a newer version.

---

## Opening the Tool

After installation, go to the Unity menu bar and open:

```
GlyphLabs > Pristine Pipeline
```

---

## 🚀 How to Use

Each tool in Pristine Pipeline works with **profiles** or **templates** — these are small files that store your settings and rules. The package comes with **built-in defaults** to help you get started right away. You can use these as-is, duplicate and customize them, or create your own from scratch.

The tool is designed around a daily-use workflow: once a profile is configured, the action button — **Generate**, **Organize**, or **Reprocess** — is the first thing you see at the top of each tab.

---

### 🧠 Active Root

Pristine Pipeline works inside a selected folder called the Active Root.

By default, this is : Assets/

You can change it anytime from the top of the tool window.

All folders and rules you create will work inside this root.

#### Why this matters

- All folder paths in templates and profiles are **relative to Active Root**
- Makes profiles **portable across projects**
- Prevents accidental asset clutter across the project
- Allows working in isolated project spaces (e.g. `Assets/GameA`, `Assets/TestScene`)

#### Quick actions

From the Active Root bar, you can:

- **Ping** — locate the root in the Project window
- **Change** — select a different folder as root
- **New Folder** — create a new project root instantly
- **Reset** — revert back to `Assets`

> 💡 Think of Active Root as the “working directory” for your entire pipeline.

#### ⚠️ Safety & Scope

Pristine Pipeline only operates inside the Active Root.

- ⚠️ Organizing assets moves files in your project.
- Only assets inside the defined scope will be affected.
- Always verify your rules and Active Root before running it.

> This prevents accidental modification of critical project files.

---

### 📁 Folder Generator

The Folder Generator creates an entire folder structure in your project from a saved template in one click — great for starting new projects consistently. It always creates folders inside the Active Root.

To create a new project structure:

1. Use **New Folder** in the Active Root bar (e.g. GameA)
2. Set it as Active Root
3. Generate your template inside it


**Using a built-in template:**
- Open the **Folder Generator** tab
- A default template is already selected in the dropdown — click **▶ Generate Folder Structure** to apply it immediately

**Creating your own template:**
- Expand **Manage Templates** at the bottom of the tab
- Click **New Template**
- Give it a name and add your folder paths one by one (e.g. `Art/Textures`, `Audio/SFX`, `Scenes`)
- Use `/` to create nested folders — e.g. `Art/Characters/Rigs` creates all three levels at once
- Click **Save Template** — it will appear in the dropdown for future use

**Sharing templates:**
- Use **Export JSON** to save a template as a `.json` file and share it with your team
- Use **Import JSON** to load a template someone else exported

---

### 🗂️ Asset Organizer

The Asset Organizer automatically moves assets to the right folder when they are imported, based on rules you define in a mapping profile.

**What gets organized:**

- Files directly under `Assets/` (top-level only)
- Everything inside your **Active Root**
- Any additional folders you manually include

> Everything else is ignored.

**Using a built-in profile:**
- Open the **Asset Organizer** tab
- Select a profile from the dropdown and toggle the organizer **on** — it will apply automatically to new imports
- Click **▶ Organize Project Now** to sort all existing assets immediately

**Creating your own profile:**
- Expand **Manage Profiles** at the bottom of the tab
- Click **New Profile**
- Add rules that map a file extension to a destination folder:
  - **Extension** — the file type to match, e.g. `png`, `wav`, `fbx`
  - **Name Pattern** *(optional)* — a wildcard filter on the filename, e.g. `T_*` to only match textures named with the `T_` prefix
  - **Destination** — the folder to move the file into, e.g. `Art/Textures` 
- **Note:** All folder paths should be defined relative to **Active Root**.
- Rules are evaluated in order — the **last matching rule wins**, so place more specific rules below general ones
- Click **Save Profile**

**Running a manual pass:**
- Click **▶ Organize Project Now** to sort all existing assets in your project using the active profile
- A confirmation dialog will appear before anything is moved — this action cannot be undone

**Sharing profiles:**
- Use **Export JSON** / **Import JSON** to share profiles across projects or with your team

**Managing Scopes:**
- You can manage additional folders from the **Scope section** inside the Asset Organizer tab.

#### Scope

The organizer does not process the entire project.

Instead, it works within a defined scope to keep your project safe.

You can expand this scope by adding folders from the UI.

> 💡 This prevents accidental modification of plugins, SDKs, and external assets.

---

### 🔧 FBX Importer

The FBX Importer automatically applies import settings to FBX files the moment they are dropped into your project, based on rules in an import profile.

**Using a built-in profile:**
- Open the **FBX Importer** tab
- Select a profile from the dropdown and toggle the importer **on** — settings will be applied automatically on every new FBX import

**Creating your own profile:**
- Expand **Manage Profiles** at the bottom of the tab
- Click **New Profile**
- Set up a **Default Preset** — these settings apply to any FBX that doesn't match a specific rule
- Add rules to handle different mesh types:
  - **Name Pattern** — a wildcard that matches FBX filenames, e.g. `SM_*` for static meshes or `SK_*` for skeletal meshes
  - **Preset settings** — scale factor, mesh compression, normals, tangents, lightmap UV generation, and more
  - **Material & texture settings** — material prefix, materials folder, textures folder
  - **Prefab settings** — prefabs folder, auto-generate prefab toggle, lightmap static toggle

- **Note:**  All material, texture, and prefab paths are defined relative to **Active Root**.
- Rules are evaluated in order — the **last matching rule wins**
- Click **Save Profile**

**Naming convention enforcement:**
- Enable **Enforce Naming Convention** to log an error and skip import settings for any FBX whose filename doesn't start with one of your defined valid prefixes (e.g. `SM_`, `SK_`, `P_`)
- When disabled, a warning is logged instead but the import still goes through

**Texture channel toggles (profile-level):**
- **Enable Emission** — search and assign `_E` / `_Emissive` textures to the Emission Map slot across all presets in the profile
- **Enable Ambient Occlusion** — search and assign `_AO` textures to the Occlusion Map slot (skipped when an ORM map is found)

**Reprocessing existing assets:**
- Click **▶ Reprocess All FBX Files** to re-apply the active profile's settings to every FBX already in your project

**Generating prefabs manually:**
- Click **Generate Prefabs Now** to create prefabs for all FBX files in the project regardless of the per-preset auto-generate toggle
- Existing prefabs at the destination path are never overwritten

**Sharing profiles:**
- Use **Export JSON** / **Import JSON** to share profiles across projects or with your team

---


## Settings

Open the **Settings** tab inside the Pristine Pipeline window to configure:

| Setting | Description |
|---|---|
| Folder Template Save Path | Where your folder templates are stored in the project |
| Asset Profile Map Save Path | Where your organizer profiles are stored |
| FBX Import Profile Save Path | Where your FBX import profiles are stored |
| Reset All Settings | Clears all saved preferences on your machine (does not delete assets) |

---

## License

This project is licensed under the **MIT License** — you're free to use, modify, and distribute it, including in commercial projects. See [LICENSE](LICENSE) for full details.

---

## Dev Notes

This is an open source project. If you're a developer looking to explore the codebase, contribute, or set up the project locally, see [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

---

## Author

Made by **Varun Anirudh Tirunagari** · [GlyphLabs](https://github.com/VarunAnirudh1811-Git)
