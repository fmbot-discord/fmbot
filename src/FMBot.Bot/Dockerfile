#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging

FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["FMBot.Bot/FMBot.Bot.csproj", "FMBot.Bot/"]
COPY ["FMBot.Youtube.Domain/FMBot.Youtube.Domain.csproj", "FMBot.Youtube.Domain/"]
COPY ["FMBot.Persistence.Domain/FMBot.Persistence.Domain.csproj", "FMBot.Persistence.Domain/"]
COPY ["FMBot.Domain/FMBot.Domain.csproj", "FMBot.Domain/"]
COPY ["FMBot.Persistence.EntityFrameWork/FMBot.Persistence.EntityFrameWork.csproj", "FMBot.Persistence.EntityFrameWork/"]
COPY ["FMBot.Youtube/FMBot.Youtube.csproj", "FMBot.Youtube/"]
COPY ["FMBot.LastFM/FMBot.LastFM.csproj", "FMBot.LastFM/"]
COPY ["FMBot.LastFM.Domain/FMBot.LastFM.Domain.csproj", "FMBot.LastFM.Domain/"]
COPY ["FMBot.Logger/FMBot.Logger.csproj", "FMBot.Logger/"]
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
