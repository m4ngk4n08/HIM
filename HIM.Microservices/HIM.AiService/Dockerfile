# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 as build
WORKDIR /src

# Copy only the project file to cache the restore step
COPY ["HIM.AiService.csproj", "./"]
RUN dotnet restore "HIM.AiService.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "HIM.AiService.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Explicitly copy data and models into the final container
COPY knowledge-base.json .
COPY Models/ ./Models/

# Expose the internal API port
EXPOSE 8080
ENTRYPOINT [ "dotnet", "HIM.AiService.dll" ]