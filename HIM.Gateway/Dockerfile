# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["HIM.Gateway.csproj", "./"]
RUN dotnet restore "HIM.Gateway.csproj"
COPY . .
RUN dotnet publish "HIM.Gateway.csproj" -c Release -o /app/publish

# Stage 2: Runtime
from mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose the standard SSH port
EXPOSE 22
ENTRYPOINT [ "dotnet", "HIM.Gateway.dll" ]