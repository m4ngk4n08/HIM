# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["HIM.AiService.csproj", "./"]
RUN dotnet restore "HIM.AiService.csproj"
COPY . .
RUN dotnet publish "HIM.AiService.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy the published app
COPY --from=build /app/publish .

# Copy extra files (knowledge base and ONNX models)
COPY knowledge-base.json .
COPY Models/ ./Models/

# Ensure /app is owned by the existing 'app' user (UID 1000)
RUN chown -R app:app /app

# Switch to the non‑root user 'app'
USER app

EXPOSE 8080
ENTRYPOINT [ "dotnet", "HIM.AiService.dll" ]