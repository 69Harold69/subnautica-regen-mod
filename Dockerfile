FROM mcr.microsoft.com/dotnet/sdk:8.0
RUN apt-get update && apt-get install -y procps && rm -rf /var/lib/apt/lists/*
WORKDIR /app
CMD ["dotnet", "build", "-c", "Release"]
