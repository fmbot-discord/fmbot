#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:9.0-noble-amd64 AS base
RUN apt-get update -y && apt-get install -y libgdiplus software-properties-common curl
RUN add-apt-repository ppa:ubuntuhandbook1/ffmpeg7 && apt update -y && apt install ffmpeg -y

WORKDIR /app

#####################
#PUPPETEER RECIPE
#####################
# Install latest chrome dev package and fonts to support major charsets (Chinese, Japanese, Arabic, Hebrew, Thai and a few others)
# Note: this installs the necessary libs to make the bundled version of Chromium that Puppeteer
# installs, work.
RUN apt-get update && apt-get -f install && apt-get -y install wget gnupg2 apt-utils
RUN wget --no-verbose -O /tmp/chrome.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && apt-get update \
    && apt-get install -y /tmp/chrome.deb --no-install-recommends --allow-downgrades fonts-ipafont-gothic fonts-noto-color-emoji fonts-wqy-zenhei fonts-thai-tlwg fonts-kacst fonts-freefont-ttf \
    && rm /tmp/chrome.deb

#####################
#END PUPPETEER RECIPE
#####################

ENV PUPPETEER_EXECUTABLE_PATH "/usr/bin/google-chrome-stable"

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Install dotnet diagnostic tools
RUN dotnet tool install --global dotnet-dump
RUN dotnet tool install --global dotnet-counters
RUN dotnet tool install --global dotnet-trace
RUN dotnet tool install --global dotnet-gcdump

COPY ["src/FMBot.Bot", "FMBot.Bot/"]
COPY ["src/FMBot.Images", "FMBot.Images/"]
COPY ["src/FMBot.Persistence.Domain", "FMBot.Persistence.Domain/"]
COPY ["src/FMBot.Domain", "FMBot.Domain/"]
COPY ["src/FMBot.Persistence", "FMBot.Persistence/"]
COPY ["src/FMBot.Persistence.EntityFrameWork", "FMBot.Persistence.EntityFrameWork/"]
COPY ["src/FMBot.Youtube", "FMBot.Youtube/"]
COPY ["src/FMBot.LastFM", "FMBot.LastFM/"]
COPY ["src/FMBot.LastFM.Domain", "FMBot.LastFM.Domain/"]
COPY ["src/FMBot.Subscriptions", "FMBot.Subscriptions/"]
COPY ["src/FMBot.Discogs", "FMBot.Discogs/"]
COPY ["src/Shared.Domain", "Shared.Domain/"]
COPY ["src/FMBot.AppleMusic", "FMBot.AppleMusic/"]
RUN dotnet restore "FMBot.Bot/FMBot.Bot.csproj"
COPY . .
WORKDIR "/src/FMBot.Bot"
RUN dotnet build "FMBot.Bot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FMBot.Bot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools

ENV PATH="/root/.dotnet/tools:${PATH}"
ENV DOTNET_EnableDiagnostics=1

ENTRYPOINT ["dotnet", "FMBot.Bot.dll"]

HEALTHCHECK --interval=20s --timeout=20s --retries=90 CMD find healthcheck -mmin -1