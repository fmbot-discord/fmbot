#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
RUN apt-get update && apt-get install -y libgdiplus
WORKDIR /app

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

#####################
#PUPPETEER RECIPE
#####################
# Install latest chrome dev package and fonts to support major charsets (Chinese, Japanese, Arabic, Hebrew, Thai and a few others)
# Note: this installs the necessary libs to make the bundled version of Chromium that Puppeteer
# installs, work.
RUN apt-get update && apt-get -f install && apt-get -y install wget gnupg2 apt-utils
RUN wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add - \
    && sh -c 'echo "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main" >> /etc/apt/sources.list.d/google.list' \
    && apt-get update \
    && apt-get install -y google-chrome-unstable fonts-ipafont-gothic fonts-wqy-zenhei fonts-thai-tlwg fonts-kacst ttf-freefont \
      --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*
#####################
#END PUPPETEER RECIPE
#####################
ENV PUPPETEER_EXECUTABLE_PATH "/usr/bin/google-chrome-unstable"