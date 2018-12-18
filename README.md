# WordPressBackup

The goal of this project is to make a simple way to download all the files and data that make up a wordpress website.

>NOTE: Depending on the size of your site and the speed of your network a complete backup can take a while (i.e. 30+ min). 

## Get the applicaiton here

```
https://dgcbackuparchive.blob.core.windows.net/app/WordPressBackup.zip
```

## Sample PowerShell Script To make a backup

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

## Get help on all the possible arguments by running

```
wordpressbackup.exe -h
```
