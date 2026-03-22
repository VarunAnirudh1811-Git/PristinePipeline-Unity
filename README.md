# Pristine Pipeline ![Version](https://img.shields.io/github/v/release/VarunAnirudh1811-Git/PristinePipeline-Unity?label=Version&color=blue)



![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity&logoColor=white) ![License](https://img.shields.io/badge/License-MIT-green.svg)



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

- Reprocess all FBX files in your project in one click

- Import and export profiles as JSON



---



## Installation



> **Important:** Pristine Pipeline must be installed through the Unity Package Manager using one of the methods below. If installed incorrectly, the built-in default profiles and templates that come with the package will not be accessible inside the tool.



> **Requires:** Unity 2021.3 or later



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

https://github.com/VarunAnirudh1811-Git/PristinePipeline-Unity.git#v1.0.0

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



---



### 📁 Folder Generator



The Folder Generator creates an entire folder structure in your project from a saved template in one click — great for starting new projects consistently.



**Using a built-in template:**

- Open the **Folder Generator** tab

- A default template is already selected in the dropdown — click **Generate Folder Structure** to apply it immediately



**Creating your own template:**

- Click **New Template**

- Give it a name and add your folder paths one by one (e.g. `Art/Textures`, `Audio/SFX`, `Scenes`)

- Use `/` to create nested folders — e.g. `Art/Characters/Rigs` creates all three levels at once

- Click **Save Template** — it will appear in the dropdown for future use



**Generation options:**

- Enable **Use Project Root Folder** and enter a project name to nest everything under `Assets/<YourProjectName>` instead of directly under `Assets/`

- Enable **Add .keep in Empty Folders** to place a small placeholder file in each empty folder so Git tracks them



**Sharing templates:**

- Use **Export JSON** to save a template as a `.json` file and share it with your team

- Use **Import JSON** to load a template someone else exported



---



### 🗂️ Asset Organizer



The Asset Organizer automatically moves assets to the right folder when they are imported, based on rules you define in a mapping profile.



**Using a built-in profile:**

- Open the **Asset Organizer** tab

- Select a profile from the dropdown and toggle the organizer **on** — it will apply automatically to new imports



**Creating your own profile:**

- Click **New Profile**

- Add rules that map a file extension to a destination folder:

&#x20; - **Extension** — the file type to match, e.g. `png`, `wav`, `fbx`

&#x20; - **Name Pattern** *(optional)* — a wildcard filter on the filename, e.g. `T_*` to only match textures named with the `T_` prefix

&#x20; - **Destination** — the folder to move the file into, e.g. `Assets/Art/Textures`

- Rules are evaluated in order — the **last matching rule wins**, so place more specific rules below general ones

- Click **Save Profile**



**Running a manual pass:**

- Click **Organize Project Now** to sort all existing assets in your project using the active profile

- A confirmation dialog will appear before anything is moved — this action cannot be undone



**Sharing profiles:**

- Use **Export JSON** / **Import JSON** to share profiles across projects or with your team



---



### 🔧 FBX Importer



The FBX Importer automatically applies import settings to FBX files the moment they are dropped into your project, based on rules in an import profile.



**Using a built-in profile:**

- Open the **FBX Importer** tab

- Select a profile from the dropdown and toggle the importer **on** — settings will be applied automatically on every new FBX import



**Creating your own profile:**

- Click **New Profile**

- Set up a **Default Preset** — these settings apply to any FBX that doesn't match a specific rule

- Add rules to handle different mesh types:

&#x20; - **Name Pattern** — a wildcard that matches FBX filenames, e.g. `SM_*` for static meshes or `SK_*` for skeletal meshes

&#x20; - **Preset settings** — scale factor, mesh compression, normals, tangents, lightmap UV generation, and more

- Rules are evaluated in order — the **last matching rule wins**

- Click **Save Profile**



**Naming convention enforcement:**

- Enable **Enforce Naming Convention** to block any FBX import whose filename doesn't start with one of your defined valid prefixes (e.g. `SM_`, `SK_`, `P_`)

- When disabled, a warning is logged instead but the import still goes through



**Reprocessing existing assets:**

- Click **Reprocess All FBX Files** to re-apply the active profile's settings to every FBX already in your project



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



## Author



Made by **Varun Anirudh Tirunagari** · [GlyphLabs](https://github.com/VarunAnirudh1811-Git)

