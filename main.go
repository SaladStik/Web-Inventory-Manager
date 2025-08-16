package main

import (
	"context"
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

	"github.com/google/uuid"
	redis "github.com/redis/go-redis/v9"
)

type Config struct {
	ImageDirectory string `json:"imageDirectory"`
}

var (
	appDB       *DB
	config      Config
	clients     = make(map[chan []byte]bool)
	mu          sync.Mutex
	redisClient *redis.Client
	ctx         = context.Background()
)

type contextKey string

const userContextKey contextKey = "username"

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
		token := uuid.NewString()
		if err := redisClient.Set(ctx, "session:"+token, creds.Username, 24*time.Hour).Err(); err != nil {
			http.Error(w, "internal error", http.StatusInternalServerError)
			return
		}
		json.NewEncoder(w).Encode(map[string]string{"message": "Login successful", "token": token})
	} else {
		http.Error(w, "Invalid username or password", http.StatusUnauthorized)
	}
}

func authMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		auth := r.Header.Get("Authorization")
		if !strings.HasPrefix(auth, "Bearer ") {
			http.Error(w, "Unauthorized", http.StatusUnauthorized)
			return
		}
		token := strings.TrimPrefix(auth, "Bearer ")
		username, err := redisClient.Get(ctx, "session:"+token).Result()
		if err != nil {
			http.Error(w, "Unauthorized", http.StatusUnauthorized)
			return
		}
		ctx := context.WithValue(r.Context(), userContextKey, username)
		next.ServeHTTP(w, r.WithContext(ctx))
	})
}

func adminMiddleware(next http.Handler) http.Handler {
	return authMiddleware(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		username := r.Context().Value(userContextKey).(string)
		isAdmin, err := appDB.IsAdmin(username)
		if err != nil || !isAdmin {
			http.Error(w, "Forbidden", http.StatusForbidden)
			return
		}
		next.ServeHTTP(w, r)
	}))
}

func createUserHandler(w http.ResponseWriter, r *http.Request) {
	var u struct {
		Username string `json:"username"`
		Password string `json:"password"`
		IsAdmin  bool   `json:"isAdmin"`
	}
	if err := json.NewDecoder(r.Body).Decode(&u); err != nil || u.Username == "" || u.Password == "" {
		http.Error(w, "invalid body", http.StatusBadRequest)
		return
	}
	if err := appDB.AddUser(u.Username, u.Password, u.IsAdmin); err != nil {
		http.Error(w, err.Error(), http.StatusBadRequest)
		return
	}
	w.WriteHeader(http.StatusCreated)
	json.NewEncoder(w).Encode(map[string]string{"message": "user created"})
}

func deleteUserHandler(w http.ResponseWriter, r *http.Request) {
	username := strings.TrimPrefix(r.URL.Path, "/api/admin/users/")
	if username == "" {
		http.Error(w, "username required", http.StatusBadRequest)
		return
	}
	if err := appDB.DeleteUser(username); err != nil {
		http.Error(w, err.Error(), http.StatusBadRequest)
		return
	}
	w.WriteHeader(http.StatusNoContent)
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

	redisClient = redis.NewClient(&redis.Options{Addr: "localhost:6379"})
	if err := redisClient.Ping(ctx).Err(); err != nil {
		log.Fatalf("redis connection: %v", err)
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/login", loginHandler)
	mux.Handle("/api/get-data", authMiddleware(http.HandlerFunc(getDataHandler)))
	mux.Handle("/api/update-config", adminMiddleware(http.HandlerFunc(updateConfigHandler)))
	mux.Handle("/api/get-serial-numbers", authMiddleware(http.HandlerFunc(getSerialNumbersHandler)))
	mux.Handle("/api/add-product", authMiddleware(http.HandlerFunc(addProductHandler)))
	mux.Handle("/api/admin/users", adminMiddleware(http.HandlerFunc(createUserHandler)))
	mux.Handle("/api/admin/users/", adminMiddleware(http.HandlerFunc(deleteUserHandler)))
	mux.Handle("/events", authMiddleware(http.HandlerFunc(eventsHandler)))
	mux.HandleFunc("/images/", imagesHandler)
	mux.Handle("/", http.FileServer(http.Dir("public")))

	go broadcastLoop()

	log.Println("Server running at http://localhost:3000/")
	log.Fatal(http.ListenAndServe(":3000", mux))
}
