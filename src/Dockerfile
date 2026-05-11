FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-amd64 AS base

RUN apt-get update -y && apt-get install -y libjemalloc2 libgdiplus software-properties-common curl
RUN add-apt-repository ppa:ubuntuhandbook1/ffmpeg8 && apt-get update -y && apt-get install -y ffmpeg

#####################
#PUPPETEER RECIPE
#####################
RUN apt-get install -y wget gnupg2 apt-utils
RUN wget --no-verbose -O /tmp/chrome.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && apt-get install -y /tmp/chrome.deb --no-install-recommends --allow-downgrades fonts-ipafont-gothic fonts-noto-color-emoji fonts-wqy-zenhei fonts-thai-tlwg fonts-kacst fonts-freefont-ttf \
    && rm /tmp/chrome.deb \
    && rm -rf /var/lib/apt/lists/*
#####################
#END PUPPETEER RECIPE
#####################

ENV LD_PRELOAD=/usr/lib/x86_64-linux-gnu/libjemalloc.so.2
ENV PUPPETEER_EXECUTABLE_PATH="/usr/bin/google-chrome-stable"

WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN dotnet tool install --global dotnet-dump \
    && dotnet tool install --global dotnet-counters \
    && dotnet tool install --global dotnet-trace \
    && dotnet tool install --global dotnet-gcdump

COPY ["src/FMBot.Bot/FMBot.Bot.csproj", "FMBot.Bot/"]
COPY ["src/FMBot.Images/FMBot.Images.csproj", "FMBot.Images/"]
COPY ["src/FMBot.Persistence.Domain/FMBot.Persistence.Domain.csproj", "FMBot.Persistence.Domain/"]
COPY ["src/FMBot.Domain/FMBot.Domain.csproj", "FMBot.Domain/"]
COPY ["src/FMBot.Persistence/FMBot.Persistence.csproj", "FMBot.Persistence/"]
COPY ["src/FMBot.Persistence.EntityFrameWork/FMBot.Persistence.EntityFrameWork.csproj", "FMBot.Persistence.EntityFrameWork/"]
COPY ["src/FMBot.LastFM/FMBot.LastFM.csproj", "FMBot.LastFM/"]
COPY ["src/FMBot.LastFM.Domain/FMBot.LastFM.Domain.csproj", "FMBot.LastFM.Domain/"]
COPY ["src/FMBot.Subscriptions/FMBot.Subscriptions.csproj", "FMBot.Subscriptions/"]
COPY ["src/FMBot.Discogs/FMBot.Discogs.csproj", "FMBot.Discogs/"]
COPY ["src/Shared.Domain/Shared.Domain.csproj", "Shared.Domain/"]
COPY ["src/FMBot.AppleMusic/FMBot.AppleMusic.csproj", "FMBot.AppleMusic/"]
RUN dotnet restore "FMBot.Bot/FMBot.Bot.csproj"
COPY src/ ./
WORKDIR "/src/FMBot.Bot"

FROM build AS publish
RUN dotnet publish "FMBot.Bot.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools

ENV PATH="/root/.dotnet/tools:${PATH}"
ENV DOTNET_GCConserveMemory=5
ENV DOTNET_GCHighMemPercent=75

ENTRYPOINT ["dotnet", "FMBot.Bot.dll"]

HEALTHCHECK --interval=20s --timeout=20s --retries=18 --start-period=180s CMD test -n "$(find healthcheck -mmin -1)"