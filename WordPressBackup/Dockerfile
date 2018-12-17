FROM microsoft/dotnet:2.1-sdk
WORKDIR /app

# copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# copy and build everything else
COPY . ./
RUN dotnet publish -c Release -o out
ENTRYPOINT ["dotnet", "out/WordPressBackup.dll", "-retrys", "$Retrys", "-file", "$File", "-dir", $Dir, "-ftphost", "$FTPHost", "-ftpuser", "$FTPUser", "-ftppwd", "$FTPPwd", "-ftpremote", "$FTPRemote", "-dbserver", "$DBServer", "-dbname", "$DBName", "-dbuser", "$DBUser", "-dbpwd", "$DBPwd", "-azconnection", "$AZConnection", "-azcontainer", "$AZContainer"]