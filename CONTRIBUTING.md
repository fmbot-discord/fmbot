# Contributing and selfhosting

We're currently not offering support for selfhosters (people who wish to run the bot locally) nor can we help with getting the database to run locally. 

As for code contributions, this depends. You can try submitting a PR for small code fixes. Larger contributions are more by invitation and are only recommended after consultation with the developer. 

The code is source-available under an Apache 2.0 with Commons Clause license. The goal of the source being available is for users to be able to see how the bot works behind the scenes.

It's recommended to open a ticket with changes you want to make [our support Discord](https://discord.gg/fmbot). This is to make sure we don't waste eachothers time. This is a large project, not all parts are publicly available and all changes have to be considered for being suitable to run on a large scale.

## Release process

1. When a new change is made, please create a PR for the dev branch.
2. The change will be tested on the develop version of the bot and if everything 
goes smoothly it will be released onto the main bot and merged to master.

Note: Since most of the time only one developer is working on the bot changes get 
committed directly to the 'dev' branch. If more developers are willing to help 
with this project, there will probably a more strict contributing guideline.

## Setting up PostgreSQL
1. Download PostgreSQL 18 and start the installation.
2. Make sure pgadmin is checked in the installation wizard.
3. If you enter a custom password, make sure to also add it to the `config.json` file later.
4. Set the port to port '5432' and continue with the installation. You can also change this port in the config if you want.
5. After the installation is done, open pgadmin.
6. Right-click on databases and create a database called 'fmbot'

## Internal API

The bot requires an internal API for some cache-heavy tasks. 

This API is not publicly available, you should however be able to run and debug the bot without it for local development.

## Getting API keys

The first time you debug the bot it will automatically create a `config.json` that's located in the `bin` folder. You will have to enter your own configuration values there.

### Discord

1. Go to the [Discord Developers Portal](https://discord.com/developers/applications)
2. Create a bot and enter your token into the config file.

### Last.fm

1. Create an [API account](https://www.last.fm/api/account/create)
2. Enter the key and secret into the config file.

### Genius

1. Go to your [API Clients](https://genius.com/api-clients)
2. Create one and enter the 'client access token' into the config file.

### Spotify

1. Go to the [Spotify Developer dashboard](https://developer.spotify.com/dashboard/applications)
2. Create an app and enter both the ID and the secret into the config file.
