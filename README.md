<h1>FMBot_Discord</h1>

[![Build status](https://ci.appveyor.com/api/projects/status/7bl2caa1wcpp9yta?svg=true)](https://ci.appveyor.com/project/Bitl/fmbot-discord)

![Logo](https://github.com/Bitl/FMBot_Discord/raw/master/BinaryFiles/avatar.png)

Discord bot built with Discord.NET in C# mostly focused on LastFM functions.

Feel free to join the Discord server here: https://discord.gg/srmpCaa

We also host the bot, [click here to add](https://discordapp.com/oauth2/authorize?client_id=356268235697553409&scope=bot&permissions=0)

<h1>Download Binaries</h1>
Get them from https://github.com/Bitl/FMBot_Discord/releases (Stable) or https://ci.appveyor.com/project/Bitl/fmbot-discord/build/artifacts (Latest).

<h1>Getting started</h1>

[Create a discord bot here.](https://discordapp.com/developers/applications/me)

[And an LastFM API account.](https://www.last.fm/api/account/create) 

This bot also supports the [Vultr VPS API](https://www.vultr.com/api/) and the [Spotify API](https://beta.developer.spotify.com/dashboard/applications), however you do not need to use this if you don't have to.

Next, download the following files from the release:

```
BinaryFiles.zip
BinaryRelease.zip
config.json
```

Extract both BinaryRelease.zip and BinaryFiles.zip and put the files from the extracted BinaryFiles folder and the config.json into your BinaryRelease folder. 

Open the config.json file with a text editor like [Notepad++](https://notepad-plus-plus.org/) and enter the token in the 'token' field, and the keys and secrets in the other fields.

Please note that the following parts of the file are completely 
optional and are not required to be edited, however some commands
and functions of the bot may not work:

```
"vultrkey"
"vultrsubid"
"baseserver"
"announcementchannel"
"featuredchannel"
"spotifykey"
"spotifysecret"
"exceptionchannel"
```

Please also launch the bot from the "StartFMBot.bat" as it allows
the bot to restart itself when there is an error.

If you want to update the bot in the future, just download the new "BinaryRelease.zip" and override the old files with the new ones.
