const { Client } = require('pg');
const crypto = require('crypto');

// Database connection configuration
const client = new Client({
    user: 'postgres',
    host: 'localhost',
    database: 'postgres',
    password: 'admin',
    port: 5432, // Default PostgreSQL port
});

client.connect();

module.exports = {
    validateUser: async (username, password) => {
        const query = 'SELECT password FROM users WHERE LOWER(username) = $1';
        const values = [username.toLowerCase()];

        try {
            console.log('Executing query:', query);
            console.log('With values:', values);

            const res = await client.query(query, values);
            console.log('Query result:', res.rows);

            if (res.rows.length === 0) {
                console.log('No user found with the provided username.');
                return false;
            }

            const userRow = res.rows[0];
            const storedPasswordHash = userRow.password;

            console.log('Stored password hash:', storedPasswordHash);

            // Directly compare the provided password with the stored password hash
            const isValid = storedPasswordHash === password;
            console.log(`IsValid: ${isValid}`);
            return isValid;
        } catch (err) {
            console.error('Error during validation:', err);
            return false;
        }
    },

    getProductData: async () => {
        const query = `
            SELECT p.id, p.model_number, p.alias, p.type, p.quantity, p.barcode, p.require_serial_number, p.image_url, s.name AS supplier, p.supplier_link, p.min_stock, p.bin 
            FROM product p 
            LEFT JOIN supplier s ON p.supplier_id = s.id 
            ORDER BY p.id ASC`;

        try {
            const res = await client.query(query);
            return res.rows;
        } catch (err) {
            console.error('Error fetching product data:', err);
            throw err;
        }
    }
};