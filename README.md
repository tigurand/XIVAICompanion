[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Y8Y81QD26O)
# XIV AI Companion
At first, I just wanted to change 2 lines of code from Eisenhuth's [dalamud-chatgpt](https://github.com/Eisenhuth/dalamud-chatgpt) so I can use Gemini instead of ChatGPT, but I changed too much...

# Installation
Add<br />
```
https://raw.githubusercontent.com/tigurand/DalamudPlugins/refs/heads/main/repo.json
```
to list of custom repo under Experimental tab in dalamud settings.  
Install and enable XIV AI Companion.

# Usage
**Requires Google API key** - click the button in the configuration which leads here: https://aistudio.google.com/app/apikey<br />
It is now using Gemini because... no real reason, just personal preference based on my wallet condition.<br />
You can use the free tier for this API key.<br />
**Warning:** Avoid sharing sensitive, confidential, or personal info with this AI, especially if you are using the free tier. It’s Google’s AI—if you are not new to the internet, you probably know how they handle your data. Check their terms here: https://ai.google.dev/gemini-api/terms

# Features
**Chat With AI**<br />
You can set the name of your AI and how your AI will address you. You can also use prompt for advanced persona. Define it yourself if you want it to be censored or uncensored. I'm not your government, I'm also not your credit card company, so I don't force censorship to you. Sometimes prohibited words may still get blocked by the system, in that case, retry your message, it could be just a hiccup. If it's persistent, try to rephrase it slightly, Google probably think that phrase is too sensitive, I have no control over this.<br />
Example:<br />
<img width="818" height="232" alt="Screenshot 2025-07-31 111107" src="https://github.com/user-attachments/assets/11b4e29e-b13d-4d64-bb62-788d2c81eb57" /><br />
<img width="967" height="289" alt="Screenshot 2025-07-31 111330" src="https://github.com/user-attachments/assets/0963a8fc-82c8-472e-9d0c-1ec588acbf80" />

Depending on your prompt, you can create a chat companion, translation tool, etc. Be creative!

**Dedicated Chat Window**<br />
By default, it's using in-game chat Debug channel (or whatever your Dalamud setting is). You can also open this plugin's Chat Window to chat with the AI.

**Send AI Chat to In-game Chat**<br />
You can copy or forward your conversation to in-game chat by right-clicking the message you want to share in Chat Window, then choose one of the menu. You can also use Auto RP function to automatically chat using AI.<br />
**Warning:** Any kind of automation has high risk. Use at your own risk.

**Adventuring with Companion**<br />
Turn your minion into a companion using Glamourer design. Glamourer needs to be installed to use this feature.

## Acknowledgements
This project was originally forked from and inspired by Eisenhuth's [ChatGPT for FFXIV](https://github.com/Eisenhuth/dalamud-chatgpt). While the core has been significantly rewritten to use Google's Gemini API and a different feature set, this project would not have been possible without their foundational work.

The advanced RGB color picker functionality was made possible by open-source code of the [Honorific](https://github.com/Caraxi/Honorific) plugin by Caraxi. Thank you for your foundational work.

Minion to companion feature was inspired by Sebane1's [Artemis Roleplaying Kit](https://github.com/Caraxi/Honorific). It's an awesome plugin.
