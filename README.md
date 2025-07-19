# XIV AI Companion
At first, I just wanted to change 2 lines of code from Eisenhuth's [dalamud-chatgpt](https://github.com/Eisenhuth/dalamud-chatgpt) so I can use Gemini instead of ChatGPT, but I changed too much...

Instead of just a simple AI, it is now your companion!

# Installation
Add<br />
`https://raw.githubusercontent.com/tigurand/DalamudPlugins/refs/heads/main/repo.json`<br />
to list of custom repo under Experimental tab in dalamud settings.  
Install and enable XIV AI Companion.

# Usage
**Requires Google API key** - click the button in the configuration which leads here: https://aistudio.google.com/app/apikey<br />
It is now using Gemini because... no real reason, just personal preference based on my wallet condition.<br />
You can use the API for free, but you still need to input payment information to create API, then you can disable billing to change it into free tier.<br />
**Warning:** Avoid sharing sensitive, confidential, or personal info with this AI, especially if you are using the free tier. It’s Google’s AI—if you are not new to the internet, you probably know how they handle your data. Check their terms here: https://ai.google.dev/gemini-api/terms

You can set the name of your companion and how your companion will address you. You can also use prompt for advanced persona. This is unfiltered, so if you don't want to see certain answers from your companion, be careful with your prompt. Sometimes it may still get blocked by the system, but in that case, just rephrase it slightly.<br />
Example:<br />
![Screenshot 2025-06-30 021947](https://github.com/user-attachments/assets/359709e7-c171-4289-a808-27923de6d848)<br />
![Screenshot 2025-06-30 022230](https://github.com/user-attachments/assets/f0347382-0899-47bc-9866-3954906ab219)

Depending on your prompt, you can create a chat companion, translation tool, etc. Be creative!<br />
You can forward the AI's reply to the in-game chat either manually via the Chat window or automatically via the Auto RP window.

## Acknowledgements
This project was originally forked from and inspired by Eisenhuth's [dalamud-chatgpt](https://github.com/Eisenhuth/dalamud-chatgpt). While the core has been significantly rewritten to use Google's Gemini API and a different feature set, this project would not have been possible without their foundational work.

The advanced RGB color picker functionality was made possible by open-source code of the [Honorific](https://github.com/Caraxi/Honorific) plugin by Caraxi. Thank you for your foundational work.
