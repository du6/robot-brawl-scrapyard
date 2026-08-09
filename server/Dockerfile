# Robot Brawl ladder API. §7 rule 1: everything ships as a Docker image.
# The restore happens INSIDE the build, so the image builds anywhere with a
# network — Cloud Build, a laptop, a CI runner — and needs nothing local.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY RobotBrawl.Api/RobotBrawl.Api.csproj RobotBrawl.Api/
RUN dotnet restore RobotBrawl.Api/RobotBrawl.Api.csproj
COPY RobotBrawl.Api/ RobotBrawl.Api/
RUN dotnet publish RobotBrawl.Api/RobotBrawl.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Cloud Run hands the port in as $PORT; default for local runs.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
# Never root: the one process here reads a database and writes blobs.
# The .NET 8 aspnet image already ships a non-root `app` user, so creating it
# unconditionally aborts the build. Create it only if it is genuinely absent,
# which keeps this working on both 8.x and any older base.
RUN id -u app >/dev/null 2>&1 || useradd -u 10001 -m app
RUN mkdir -p /var/lib/robotbrawl/blobs && chown -R app /var/lib/robotbrawl
USER app
ENTRYPOINT ["dotnet", "RobotBrawl.Api.dll"]
