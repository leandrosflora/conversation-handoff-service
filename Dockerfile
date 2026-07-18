FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY conversation-handoff-service.csproj .
RUN dotnet restore conversation-handoff-service.csproj

COPY . .
RUN dotnet publish conversation-handoff-service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "conversation-handoff-service.dll"]
