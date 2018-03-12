<h1>FMBot_Discord</h1>

![Logo](https://github.com/Bitl/FMBot_Discord/raw/master/avatar.png)

Discord bot built with Discord.NET in C# mostly focused on LastFM functions.

Feel free to join the Discord server here: https://discord.gg/srmpCaa

We also host the bot, [click here to add](https://discordapp.com/oauth2/authorize?client_id=356268235697553409&scope=bot&permissions=0)

### Getting started

[Create a discord bot here.](https://discordapp.com/developers/applications/me)

[And an LastFM API account.](https://www.last.fm/api/account/create) 

This bot also supports the [Vultr VPS API](https://www.vultr.com/api/) and the [Spotify API](https://beta.developer.spotify.com/dashboard/applications), however you do not need to use this if you don't have to.

Open the config.json file and enter the token in the 'token' field, and the keys and secrets in the other fields.

Please note that the following parts of the file are completely 
optional and are not required to be edited, however some commands
and functions of the bot may not work:

"vultrkey"
"vultrsubid"
"baseserver"
"announcementchannel"
"featuredchannel"
"spotifykey"
"spotifysecret"
"exceptionchannel"

Please also launch the bot from the "StartFMBot.bat" as it allows
the bot to restart itself when there is an error.
