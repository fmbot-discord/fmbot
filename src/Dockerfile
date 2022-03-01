#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
RUN apt-get update && apt-get install -y libgdiplus
WORKDIR /app

#####################
#PUPPETEER RECIPE
#####################
# Install latest chrome dev package and fonts to support major charsets (Chinese, Japanese, Arabic, Hebrew, Thai and a few others)
# Note: this installs the necessary libs to make the bundled version of Chromium that Puppeteer
# installs, work.
ARG CHROME_VERSION="98.0.4758.102-1"
RUN apt-get update && apt-get -f install && apt-get -y install wget gnupg2 apt-utils
RUN wget --no-verbose -O /tmp/chrome.deb http://dl.google.com/linux/chrome/deb/pool/main/g/google-chrome-stable/google-chrome-stable_${CHROME_VERSION}_amd64.deb \
&& apt-get update \
&& apt-get install -y /tmp/chrome.deb --no-install-recommends --allow-downgrades fonts-ipafont-gothic fonts-wqy-zenhei fonts-thai-tlwg fonts-kacst fonts-freefont-ttf \
&& rm /tmp/chrome.deb

#####################
#END PUPPETEER RECIPE
#####################

ENV PUPPETEER_EXECUTABLE_PATH "/usr/bin/google-chrome-stable"

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["src/FMBot.Bot", "FMBot.Bot/"]
COPY ["src/FMBot.Images", "FMBot.Images/"]
COPY ["src/FMBot.Youtube.Domain", "FMBot.Youtube.Domain/"]
COPY ["src/FMBot.Persistence.Domain", "FMBot.Persistence.Domain/"]
COPY ["src/FMBot.Domain", "FMBot.Domain/"]
COPY ["src/FMBot.Persistence", "FMBot.Persistence/"]
COPY ["src/FMBot.Persistence.EntityFrameWork", "FMBot.Persistence.EntityFrameWork/"]
COPY ["src/FMBot.Youtube", "FMBot.Youtube/"]
COPY ["src/FMBot.LastFM", "FMBot.LastFM/"]
COPY ["src/FMBot.LastFM.Domain", "FMBot.LastFM.Domain/"]
COPY ["src/FMBot.Logger", "FMBot.Logger/"]
RUN dotnet restore "FMBot.Bot/FMBot.Bot.csproj"
COPY . .
WORKDIR "/src/FMBot.Bot"
RUN dotnet build "FMBot.Bot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FMBot.Bot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FMBot.Bot.dll"]