# kcp-dotnet
A [KCP](https://github.com/skywind3000/kcp) C# implementation for .net core

**CAUTION** this project is still NOT production ready.

## Prerequisites

* .net core 2.0 (https://dotnet.github.io/)
* Golang toolchain(optional) (http://golang.org)

## How to 

### Run test case

```
git clone https://github.com/ichenq/kcp-dotnet
cd kcp-dotnet
dotnet run
```

### Communicate with kcp-go

server

```
go get -v github.com/xtaci/kcp-go
go run server.go
```

client

```
dotnet run socket
```
