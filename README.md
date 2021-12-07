# kcp-dotnet
A [KCP](https://github.com/skywind3000/kcp) C# implementation for .net core

**CAUTION** this project is NOT production ready!

## Prerequisites

* .net core (https://dotnet.github.io/)
* Golang SDK(optional) (http://golang.org)

## How to Use

### Run test case

```
git clone https://github.com/qchencc/kcp-dotnet
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
