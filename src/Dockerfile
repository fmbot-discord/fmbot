FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
# Install dependencies and add FFmpeg repository
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libwebp-dev \
    webp \
    wget \
    xz-utils \
    libvpx7 \
    libx264-163 \
    libx265-199 \
    libmp3lame0 \
    libass9 \
    libwebpmux3 \
    libvorbis0a \
    libtheora0 \
    libopus0 \
    && rm -rf /var/lib/apt/lists/*

# Install latest static FFmpeg build
RUN wget https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz \
    && tar xvf ffmpeg-release-amd64-static.tar.xz \
    && mv ffmpeg-*-amd64-static/ffmpeg /usr/local/bin/ \
    && mv ffmpeg-*-amd64-static/ffprobe /usr/local/bin/ \
    && rm -rf ffmpeg-*-amd64-static* \
    && chmod +x /usr/local/bin/ffmpeg \
    && chmod +x /usr/local/bin/ffprobe

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

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
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
COPY ["src/Shared.Protos", "Shared.Protos/"]
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

ENV DOTNET_gcServer=1

ENTRYPOINT ["dotnet", "FMBot.Bot.dll"]

HEALTHCHECK --interval=20s --timeout=20s --retries=90 CMD find healthcheck -mmin -1