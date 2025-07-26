# HeartSpeak
--------------------------------------------------------
Heart Speak: Real-Time Heart Rate Narrator
--------------------------------------------------------

INSTRUCTIONS

1. Double-click HeartSpeak.exe to launch the program.   

2. When prompted:

   > press y or n to enter simple mode, if y, will disable all flavor texts and grammatical extras in TTS.

   > Enter your maximum heart rate  
   → Helps Heart Speak deliver zone-aware insights and commentary.

   > pick your desired heart rate provider and fill in its required info

--------------------------------------------------------

AVAILABLE HEART RATE PROVIDERS

- Pulsoid
    - Useful if you already use Pulsoid with your heart rate monitor

   > In the case of missing playwright browser binaries you will be prompted to install them. To proceed type in y, this should be short but once done will continue.

   > Enter your Pulsoid Overlay URL or enter the number assoicated with a saved URL. URL's are saved in a text file when entered automatically the first time.
     → Must begin with: https://  
     Example: https://pulsoid.net/widget/view/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx

    - Ensure proper Pulsoid setup:
       - Visit https://pulsoid.net
       - Connect your heart rate monitor
       - Link the monitor to a widget you want to use
       - Confirm that your widget is live and updating in real time

- File-based
  - Useful if you use something else than Pulsoid, but can get your current heart rate to a file on disk
  - The file should only contain your heart rate, as a number, and nothing else.
    - `180` is okay
    - `180 HR` is not okay
    - `9001` is okay as far as HeartSay is concerned, but you might want to check if your heart rate monitor is okay

--------------------------------------------------------

FEATURES

- Headless browser using playwright.
- Speaks your BPM aloud when there’s a significant change  
- Delivers motivational commentary based on your heart rate zone  
- Tracks time spent in each zone and offers reflective insights  
- Routes voice through your system's default audio device  
- Compatible with Voicemeeter and virtual audio setups
- Win10+ Compatible only

--------------------------------------------------------

NOTE. You need to install .NET runtimes for application to properly function

https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-8.0.18-windows-x64-installer

feel free to join the discord https://discord.gg/qpVg54ER9H

Let your heart tell the story.  
Run Heart Speak — and experience cardio with character.
