package main

import (
        "encoding/json"
        "fmt"
        "log"
        "net/http"
        "os"
        "path/filepath"
        "strconv"
        "strings"
        "sync"
        "time"
)

type Config struct {
	ImageDirectory string `json:"imageDirectory"`
}

var (
        appDB   *DB
        config  Config
        clients = make(map[chan []byte]bool)
        mu      sync.Mutex
)

func loadConfig() Config {
	data, err := os.ReadFile("config.json")
	if err != nil {
		return Config{}
	}
	var c Config
	if err := json.Unmarshal(data, &c); err != nil {
		return Config{}
	}
	return c
}

func saveConfig(c Config) error {
	data, err := json.MarshalIndent(c, "", "    ")
	if err != nil {
		return err
	}
	return os.WriteFile("config.json", data, 0644)
}

func imagesHandler(w http.ResponseWriter, r *http.Request) {
        file := strings.TrimPrefix(r.URL.Path, "/images/")
        http.ServeFile(w, r, filepath.Join(config.ImageDirectory, file))
}

func loginHandler(w http.ResponseWriter, r *http.Request) {
	var creds struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}
	if err := json.NewDecoder(r.Body).Decode(&creds); err != nil {
		http.Error(w, "invalid body", http.StatusBadRequest)
		return
	}
	ok, err := appDB.ValidateUser(creds.Username, creds.Password)
	if err != nil {
		http.Error(w, "internal error", http.StatusInternalServerError)
		return
	}
	if ok {
		json.NewEncoder(w).Encode(map[string]string{"message": "Login successful", "redirectUrl": "/main-menu.html"})
	} else {
		http.Error(w, "Invalid username or password", http.StatusUnauthorized)
	}
}

func getDataHandler(w http.ResponseWriter, r *http.Request) {
	products, err := appDB.GetProductData()
	if err != nil {
		http.Error(w, "internal error", http.StatusInternalServerError)
		return
	}
	json.NewEncoder(w).Encode(products)
}

func updateConfigHandler(w http.ResponseWriter, r *http.Request) {
	var c Config
	if err := json.NewDecoder(r.Body).Decode(&c); err != nil || c.ImageDirectory == "" {
		http.Error(w, "Invalid configuration", http.StatusBadRequest)
		return
	}
	config = c
	if err := saveConfig(c); err != nil {
		http.Error(w, "Failed to save config", http.StatusInternalServerError)
		return
	}
	json.NewEncoder(w).Encode(map[string]string{"message": "Configuration updated successfully"})
}

func getSerialNumbersHandler(w http.ResponseWriter, r *http.Request) {
	idStr := r.URL.Query().Get("id_product")
	if idStr == "" {
		http.Error(w, "Product ID is required", http.StatusBadRequest)
		return
	}
	id, err := strconv.Atoi(idStr)
	if err != nil {
		http.Error(w, "Invalid product ID", http.StatusBadRequest)
		return
	}
	nums, err := appDB.GetSerialNumbers(id)
	if err != nil {
		http.Error(w, "Error fetching serial numbers", http.StatusInternalServerError)
		return
	}
	json.NewEncoder(w).Encode(nums)
}

func addProductHandler(w http.ResponseWriter, r *http.Request) {
	var p ProductInput
	if err := json.NewDecoder(r.Body).Decode(&p); err != nil {
		http.Error(w, "invalid body", http.StatusBadRequest)
		return
	}
	id, err := appDB.AddProduct(p)
	if err != nil {
		http.Error(w, "Error adding product", http.StatusInternalServerError)
		return
	}
	json.NewEncoder(w).Encode(map[string]interface{}{"success": true, "productId": id})
}

func eventsHandler(w http.ResponseWriter, r *http.Request) {
        flusher, ok := w.(http.Flusher)
        if !ok {
                http.Error(w, "Streaming unsupported", http.StatusInternalServerError)
                return
        }
        w.Header().Set("Content-Type", "text/event-stream")
        w.Header().Set("Cache-Control", "no-cache")
        w.Header().Set("Connection", "keep-alive")

        ch := make(chan []byte)
        mu.Lock()
        clients[ch] = true
        mu.Unlock()

        ctx := r.Context()
        go func() {
                <-ctx.Done()
                mu.Lock()
                delete(clients, ch)
                mu.Unlock()
        }()

        for {
                select {
                case msg := <-ch:
                        fmt.Fprintf(w, "data: %s\n\n", msg)
                        flusher.Flush()
                case <-ctx.Done():
                        return
                }
        }
}

func broadcastLoop() {
        ticker := time.NewTicker(10 * time.Second)
        defer ticker.Stop()
        for range ticker.C {
                data, err := appDB.GetProductData()
                if err != nil {
                        continue
                }
                payload, err := json.Marshal(data)
                if err != nil {
                        continue
                }
                mu.Lock()
                for ch := range clients {
                        select {
                        case ch <- payload:
                        default:
                        }
                }
                mu.Unlock()
        }
}

func main() {
	var err error
	appDB, err = NewDB()
	if err != nil {
		log.Fatalf("db connection: %v", err)
	}
	config = loadConfig()

        mux := http.NewServeMux()
        mux.HandleFunc("/login", loginHandler)
        mux.HandleFunc("/api/get-data", getDataHandler)
        mux.HandleFunc("/api/update-config", updateConfigHandler)
        mux.HandleFunc("/api/get-serial-numbers", getSerialNumbersHandler)
        mux.HandleFunc("/api/add-product", addProductHandler)
        mux.HandleFunc("/events", eventsHandler)
        mux.HandleFunc("/images/", imagesHandler)
        mux.Handle("/", http.FileServer(http.Dir("public")))

        go broadcastLoop()

        log.Println("Server running at http://localhost:3000/")
        log.Fatal(http.ListenAndServe(":3000", mux))
}
