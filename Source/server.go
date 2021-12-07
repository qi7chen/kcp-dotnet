// Copyright (C) 2017 simon@qchen.fun All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

package main

import (
	"log"
	"net"
	"os"
	"time"

	"github.com/xtaci/kcp-go"
)

func main() {
	var host = "127.0.0.1:9527"
	if len(os.Args) > 1 {
		host = os.Args[1]
	}
	listener, err := kcp.Listen(host)
	if err != nil {
		log.Fatalf("Listen: %v", err)
	}
	log.Printf("start listen at %s\n", host)
	for {
		conn, err := listener.Accept()
		if err != nil {
			log.Fatalf("Accept: %v", err)
		}
		go handleConn(conn)
	}
}

// Echo every thing back
func handleConn(conn net.Conn) {
	defer conn.Close()
	var udpConn = conn.(*kcp.UDPSession)
	conn.SetReadDeadline(time.Now().Add(60 * time.Second))
	var buffer = make([]byte, 4096)
	for {
		bytesRead, err := conn.Read(buffer)
		if err != nil {
			log.Printf("Read: %v", err)
			break
		}
		log.Printf("%d(%v): %s\n", udpConn.GetConv(), udpConn.RemoteAddr(),  buffer[:bytesRead])
		conn.Write(buffer[:bytesRead])
	}
}
