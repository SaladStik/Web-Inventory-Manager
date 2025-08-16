package main

import (
	"database/sql"
	"errors"
	"fmt"
	"os"
	"time"

	_ "github.com/lib/pq"
	"golang.org/x/crypto/bcrypt"
)

type DB struct {
	*sql.DB
}

func NewDB() (*DB, error) {
	dsn := os.Getenv("DATABASE_URL")
	if dsn == "" {
		dsn = "postgres://postgres:postgres@localhost:5432/webinventory?sslmode=disable"
	}
	db, err := sql.Open("postgres", dsn)
	if err != nil {
		return nil, err
	}
	if err := db.Ping(); err != nil {
		return nil, err
	}
	d := &DB{db}
	if err := d.init(); err != nil {
		return nil, err
	}
	return d, nil
}

func (db *DB) init() error {
	_, err := db.Exec(`CREATE TABLE IF NOT EXISTS users (
        username TEXT PRIMARY KEY,
        password_hash BYTEA NOT NULL,
        is_admin BOOLEAN NOT NULL DEFAULT FALSE
    );

    CREATE TABLE IF NOT EXISTS products (
        id SERIAL PRIMARY KEY,
        model_number TEXT,
        alias TEXT,
        type TEXT,
        quantity INTEGER,
        barcode TEXT,
        require_serial_number BOOLEAN,
        image_url TEXT,
        supplier TEXT,
        supplier_link TEXT,
        min_stock INTEGER,
        bin TEXT
    );

    CREATE TABLE IF NOT EXISTS serial_numbers (
        id SERIAL PRIMARY KEY,
        product_id INTEGER REFERENCES products(id),
        location_name TEXT,
        serial_number TEXT,
        date TIMESTAMPTZ,
        note TEXT,
        ticket_num TEXT
    );`)
	if err != nil {
		return err
	}

	var count int
	if err := db.QueryRow("SELECT COUNT(*) FROM users WHERE username=$1", "admin").Scan(&count); err != nil {
		return err
	}
	if count == 0 {
		hash, err := bcrypt.GenerateFromPassword([]byte("admin"), bcrypt.DefaultCost)
		if err != nil {
			return err
		}
		if _, err := db.Exec("INSERT INTO users(username, password_hash, is_admin) VALUES($1,$2,$3)", "admin", hash, true); err != nil {
			return err
		}
	}
	return nil
}

func (db *DB) ValidateUser(username, password string) (bool, error) {
	var hash []byte
	err := db.QueryRow("SELECT password_hash FROM users WHERE username=$1", username).Scan(&hash)
	if errors.Is(err, sql.ErrNoRows) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	if bcrypt.CompareHashAndPassword(hash, []byte(password)) != nil {
		return false, nil
	}
	return true, nil
}

func (db *DB) AddUser(username, password string, isAdmin bool) error {
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return err
	}
	_, err = db.Exec("INSERT INTO users(username, password_hash, is_admin) VALUES($1,$2,$3)", username, hash, isAdmin)
	if err != nil {
		if pqErr(err, "23505") {
			return fmt.Errorf("user already exists")
		}
		return err
	}
	return nil
}

func (db *DB) DeleteUser(username string) error {
	res, err := db.Exec("DELETE FROM users WHERE username=$1", username)
	if err != nil {
		return err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return err
	}
	if n == 0 {
		return fmt.Errorf("user not found")
	}
	return nil
}

func (db *DB) IsAdmin(username string) (bool, error) {
	var isAdmin bool
	err := db.QueryRow("SELECT is_admin FROM users WHERE username=$1", username).Scan(&isAdmin)
	if errors.Is(err, sql.ErrNoRows) {
		return false, fmt.Errorf("user not found")
	}
	if err != nil {
		return false, err
	}
	return isAdmin, nil
}

type Product struct {
	ID                  int    `json:"id"`
	ModelNumber         string `json:"model_number"`
	Alias               string `json:"alias"`
	Type                string `json:"type"`
	Quantity            int    `json:"quantity"`
	Barcode             string `json:"barcode"`
	RequireSerialNumber bool   `json:"require_serial_number"`
	ImageURL            string `json:"image_url,omitempty"`
	Supplier            string `json:"supplier,omitempty"`
	SupplierLink        string `json:"supplier_link,omitempty"`
	MinStock            int    `json:"min_stock,omitempty"`
	Bin                 string `json:"bin,omitempty"`
}

func (db *DB) GetProductData() ([]Product, error) {
	rows, err := db.Query(`SELECT id, model_number, alias, type, quantity, barcode, require_serial_number, image_url, supplier, supplier_link, min_stock, bin FROM products`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var products []Product
	for rows.Next() {
		var p Product
		if err := rows.Scan(&p.ID, &p.ModelNumber, &p.Alias, &p.Type, &p.Quantity, &p.Barcode, &p.RequireSerialNumber, &p.ImageURL, &p.Supplier, &p.SupplierLink, &p.MinStock, &p.Bin); err != nil {
			return nil, err
		}
		products = append(products, p)
	}
	return products, rows.Err()
}

type SerialNumber struct {
	ID           int        `json:"id"`
	ProductID    int        `json:"id_product"`
	LocationName string     `json:"location_name"`
	SerialNumber string     `json:"serial_number"`
	Date         *time.Time `json:"date,omitempty"`
	Note         string     `json:"note,omitempty"`
	TicketNum    string     `json:"ticket_num,omitempty"`
}

func (db *DB) GetSerialNumbers(productID int) ([]SerialNumber, error) {
	rows, err := db.Query(`SELECT id, product_id, location_name, serial_number, date, note, ticket_num FROM serial_numbers WHERE product_id=$1`, productID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var list []SerialNumber
	for rows.Next() {
		var s SerialNumber
		var t sql.NullTime
		if err := rows.Scan(&s.ID, &s.ProductID, &s.LocationName, &s.SerialNumber, &t, &s.Note, &s.TicketNum); err != nil {
			return nil, err
		}
		if t.Valid {
			s.Date = &t.Time
		}
		list = append(list, s)
	}
	return list, rows.Err()
}

type ProductInput struct {
	ModelNumber         string `json:"modelNumber"`
	Alias               string `json:"alias"`
	Type                string `json:"type"`
	Quantity            int    `json:"quantity"`
	Barcode             string `json:"barcode"`
	RequireSerialNumber bool   `json:"requireSerialNumber"`
	ImageURL            string `json:"imageUrl"`
	Supplier            string `json:"supplier"`
	SupplierLink        string `json:"supplierLink"`
	MinStock            int    `json:"minStock"`
	Bin                 string `json:"bin"`
}

func (db *DB) AddProduct(p ProductInput) (int, error) {
	var id int
	err := db.QueryRow(`INSERT INTO products (model_number, alias, type, quantity, barcode, require_serial_number, image_url, supplier, supplier_link, min_stock, bin)
        VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) RETURNING id`,
		p.ModelNumber, p.Alias, p.Type, p.Quantity, p.Barcode, p.RequireSerialNumber, p.ImageURL, p.Supplier, p.SupplierLink, p.MinStock, p.Bin).Scan(&id)
	if err != nil {
		return 0, err
	}
	return id, nil
}

func pqErr(err error, code string) bool {
	type coder interface {
		SQLState() string
	}
	if e, ok := err.(coder); ok {
		return e.SQLState() == code
	}
	return false
}
