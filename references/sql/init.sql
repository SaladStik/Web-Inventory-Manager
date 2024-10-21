---Postgres SQL script to create the database schema and tables

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS public;

-- Set the search path to the public schema
SET search_path TO public;

-- Drop existing tables if they exist
-- DROP TABLE IF EXISTS history CASCADE;
-- DROP TABLE IF EXISTS product CASCADE;
-- DROP TABLE IF EXISTS location CASCADE;
-- DROP TABLE IF EXISTS supplier CASCADE;

-- Create tables
CREATE TABLE supplier (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL UNIQUE
);

CREATE TABLE location (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL UNIQUE
);

CREATE TABLE product (
    id SERIAL PRIMARY KEY,
    model_number VARCHAR(255) NOT NULL UNIQUE,
    alias VARCHAR(255),
    type VARCHAR(255),
    quantity INTEGER NOT NULL,
    barcode VARCHAR(255) NOT NULL UNIQUE,
    require_serial_number BOOLEAN NOT NULL,
    image_url VARCHAR(500),
    supplier_id INTEGER,
    CONSTRAINT fk_supplier_id FOREIGN KEY(supplier_id) REFERENCES supplier(id),
    supplier_link VARCHAR(500),
    min_stock INTEGER,
    str_loc VARCHAR(255),
    bin VARCHAR(50)
);

CREATE TABLE history (
    id SERIAL PRIMARY KEY,
    id_product INTEGER,
    id_location INTEGER,
    serial_number VARCHAR(255) NOT NULL UNIQUE,
    date TIMESTAMP,
    ticket_num VARCHAR(255),
    note VARCHAR(1000),
    CONSTRAINT fk_history_id_product FOREIGN KEY(id_product) REFERENCES product(id),
    CONSTRAINT fk_history_id_location FOREIGN KEY(id_location) REFERENCES location(id)
);

-- Insert default location
INSERT INTO location (name) VALUES ('Stock');

-- Insert initial suppliers
INSERT INTO supplier (name) VALUES ('Active IS');

CREATE TABLE quick_sheets (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT
);

CREATE TABLE quick_sheet_products (
    quick_sheet_id INT NOT NULL,
    product_id INT NOT NULL,
    FOREIGN KEY (quick_sheet_id) REFERENCES quick_sheets(id),
    FOREIGN KEY (product_id) REFERENCES product(id)
);

CREATE TABLE roles (
    id SERIAL PRIMARY KEY,
    role_name VARCHAR(50) UNIQUE NOT NULL
);

INSERT INTO roles (role_name) VALUES ('Administrator'), ('User'), ('Viewer');

CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    password VARCHAR(255) NOT NULL,
    first_name VARCHAR(50),
    last_name VARCHAR(50),
    role_id INTEGER NOT NULL REFERENCES roles(id),
    salt TEXT,
    forgot_password BOOLEAN DEFAULT FALSE
);

CREATE TABLE jobs (
    id SERIAL PRIMARY KEY,
    job_active BOOLEAN DEFAULT TRUE NOT NULL,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    tkt_num VARCHAR(7),
    users_id INT NOT NULL,
    date TIMESTAMP,
    FOREIGN KEY (users_id) REFERENCES users(id)
);

CREATE TABLE jobs_products (
    job_id INTEGER NOT NULL,
    product_id INTEGER NOT NULL,
    product_quantity INTEGER NOT NULL,
    product_original_quantity INTEGER NOT NULL,
    FOREIGN KEY (job_id) REFERENCES jobs(id),
    FOREIGN KEY (product_id) REFERENCES product(id)
);

CREATE TABLE event_ids(
    id SERIAL PRIMARY KEY,
    event_id VARCHAR(4) UNIQUE NOT NULL,
    event_name VARCHAR(15) UNIQUE NOT NULL
);

CREATE TABLE the_log(
    id SERIAL PRIMARY KEY,
    event_id VARCHAR(4) NOT NULL,
    users_id INTEGER NOT NULL,
    product_id INTEGER NOT NULL,
    date TIMESTAMP,
    previous_value VARCHAR(255),
    new_value VARCHAR(255),
    field_updated VARCHAR(255),
     serial_number VARCHAR(255),
    FOREIGN KEY (product_id) REFERENCES product(id),
    FOREIGN KEY (users_id) REFERENCES users(id),
    FOREIGN KEY (event_id) REFERENCES event_ids(event_id)
);

-- This may or may not end up being used for the log I kind of want to do a funny thing like "User incinerated 10 ProductID" where incinerated is the phrase so it could also be
-- "User traded 10 ProductID" and give the end users the preference of what they want it to cycle through
CREATE TABLE phrases(
    id SERIAL PRIMARY KEY,
    phrase VARCHAR(255),
    phrase_active BOOLEAN DEFAULT TRUE NOT NULL,
    -- use_phrases is the setting that basically will enable the use of phrases
    use_phrases BOOLEAN DEFAULT FALSE NOT NULL
);

-- Insert event IDs
INSERT INTO event_ids (event_id, event_name) VALUES
('E001', 'Update'),
('E002', 'Add'),
('E003', 'Create'),
('E004', 'Delete'),
('E005', 'Remove'),
('E006', 'Print Label'),
('E007', 'User Login');
