FROM mcr.microsoft.com/dotnet/core/runtime:2.2-stretch-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:2.2-stretch AS build
WORKDIR /src
COPY ["FMBot.Bot/FMBot.Bot.csproj", "FMBot.Bot/"]
COPY ["FMBot.Data/FMBot.Data.csproj", "FMBot.Data/"]
COPY ["Bot.Logger/Bot.Logger.csproj", "Bot.Logger/"]
COPY ["Bot.Logger.Interfaces/Bot.Logger.Interfaces.csproj", "Bot.Logger.Interfaces/"]
COPY ["FMBot.YoutubeSearch/FMBot.YoutubeSearch.csproj", "FMBot.YoutubeSearch/"]
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