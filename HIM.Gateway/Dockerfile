# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["HIM.Gateway.csproj", "./"]
RUN dotnet restore "HIM.Gateway.csproj"
COPY . .
RUN dotnet publish "HIM.Gateway.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Ensure the /app directory is owned by the existing 'app' user (UID 1000)
RUN chown -R app:app /app

COPY --from=build /app/publish .
RUN chown -R himuser:himgroup /app

USER himuser

EXPOSE 22
ENTRYPOINT [ "dotnet", "HIM.Gateway.dll" ]