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

# Create non‑root user (UID 1000)
RUN /usr/sbin/groupadd -g 1000 himgroup && \
    /usr/sbin/useradd -u 1000 -g himgroup -m -d /home/himuser -s /sbin/nologin himuser

COPY --from=build /app/publish .
RUN chown -R himuser:himgroup /app

USER himuser

EXPOSE 22
ENTRYPOINT [ "dotnet", "HIM.Gateway.dll" ]