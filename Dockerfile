FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY . .
RUN dotnet restore HoneyDrunk.Identity/HoneyDrunk.Identity.Api/HoneyDrunk.Identity.Api.csproj --locked-mode \
    && dotnet publish HoneyDrunk.Identity/HoneyDrunk.Identity.Api/HoneyDrunk.Identity.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
ARG RELEASE_ID=local
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Identity__ReleaseId=$RELEASE_ID
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "HoneyDrunk.Identity.Api.dll"]
