FROM mcr.microsoft.com/dotnet/sdk:11.0.100-resolute AS build
# CVE-2026-109518: bootstrap Ubuntu's APT fix with retries disabled, as Canonical recommends.
RUN apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade '?source-package(^apt$)' \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 3.2.1ubuntu0.1 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libapt-pkg7.0)" ge 3.2.1ubuntu0.1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /workspace
COPY . .
RUN dotnet restore src/HoneyDrunk.Identity.Api/HoneyDrunk.Identity.Api.csproj --locked-mode \
    && dotnet publish src/HoneyDrunk.Identity.Api/HoneyDrunk.Identity.Api.csproj \
       -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-resolute AS runtime
# CVE-2026-109518: bootstrap Ubuntu's APT fix with retries disabled, as Canonical recommends.
RUN apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade '?source-package(^apt$)' \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 3.2.1ubuntu0.1 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libapt-pkg7.0)" ge 3.2.1ubuntu0.1 \
    && rm -rf /var/lib/apt/lists/*
ARG RELEASE_ID=local
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Identity__ReleaseId=$RELEASE_ID
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "HoneyDrunk.Identity.Api.dll"]
