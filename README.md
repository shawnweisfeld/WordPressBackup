# WordPressBackup

The goal of this project is to make a simple way to download all the files and data that make up a wordpress website.

## Get the applicaiton here

```
https://dgcbackuparchive.blob.core.windows.net/app/WordPressBackup.zip
```

## Run this from powershell

```
$Retryes = 5;
$File = "mysite"
$Dir = "C:\temp"
$FTPHost = "ftppub.everleap.com"
$FTPUser = "1234-567\001234"
$FTPPwd = "blah"
$FTPRemote = "/site/wwwroot"
$DBServer = "my01.everleap.com"
$DBName = "MySQL_1234_blah"
$DBUser = "blah"
$DBPwd = "blah"

wordpressbackup.exe -retrys $Retryes -file $File  -dir $Dir `
    -ftphost $FTPHost -ftpuser $FTPUser -ftppwd $FTPPwd `
    -ftpremote $FTPRemote -dbserver $DBServer -dbname $DBName `
    -dbuser $DBUser -dbpwd $DBPwd
```
