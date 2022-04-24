# HRngLite
The backend module of HRng that allows for handling operations using HttpClient.
Being browser-less, this module can be used on all platforms; however, this means that operations can be slightly slower than their HRngSelenium counterparts (due to lack of optimization).

## Installation
This module is supposed to be built along with **HRngBackend**.
Build this project using Visual Studio or `dotnet` (refer to the solution's `README.md`).

## Usage
This module by itself is not executable; the backend (**HRngBackend**) and a frontend is needed. See the **LibTests** project for more details.

## Features
As of now, the HRng backend contains resources for:

**Internal (helper) features:**
* Helper for HttpClient functions (`HTTPHelper.cs`)
* No-login HTTP client for Pass 2 of `GetComments()` (`P2Client.cs`)

**Frontend-facing features:**
* Facebook post scraping (`FBPost.cs`)
* Facebook credentials-based login (`FBLogin.cs`)

These feature(s) are planned to be added:
* Facebook post poll scraping
* Facebook group (both posts groups and chat groups) members list retrieval

## Contributing
Pull requests are welcome.
For major changes, please open an issue first to discuss what you'd like to change.

Any feature change requires **LibTests** to be updated accordingly.