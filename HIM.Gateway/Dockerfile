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

# Create a non‑root user and group (UID/GID 1000)
RUN addgroup --gid 1000 himgroup && \
    adduser --uid 1000 --gid 1000 --disabled-password --gecos "" himuser

# Copy the published app
COPY --from=build /app/publish .

# Ensure the entire /app directory is owned by the new user
RUN chown -R himuser:himgroup /app

# Switch to the non‑root user
USER himuser

# Expose the SSH port
EXPOSE 22

ENTRYPOINT [ "dotnet", "HIM.Gateway.dll" ]