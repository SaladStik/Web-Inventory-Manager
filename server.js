const express = require('express');
const bodyParser = require('body-parser');
const path = require('path');
const fs = require('fs');
const { validateUser, getProductData } = require('./database');
const WebSocket = require('ws');
const db = require('./database'); // Adjust the path as needed

// Load configuration
const config = JSON.parse(fs.readFileSync(path.join(__dirname, 'config.json'), 'utf8'));

const app = express();
const port = 3000;

app.use(bodyParser.json());
app.use(express.static(path.join(__dirname, 'public'))); // Serve static files from the 'public' directory

// Serve images from the configured directory
const imageDirectory = config.imageDirectory;
console.log(`Serving images from: ${imageDirectory}`);

// Middleware to log image requests
app.use('/images', (req, res, next) => {
    console.log(`Image request: ${req.url}`);
    next();
});

app.use('/images', express.static(imageDirectory));

app.post('/login', async (req, res) => {
    const { username, password } = req.body;

    try {
        const isValid = await validateUser(username, password);
        if (isValid) {
            console.log(`Login successful for user: ${username}`);
            res.status(200).json({ message: 'Login successful', redirectUrl: '/main-menu.html' });
        } else {
            console.log(`Invalid login attempt for user: ${username}`);
            res.status(401).json({ message: 'Invalid username or password' });
        }
    } catch (err) {
        console.error('Error during login attempt:', err);
        res.status(500).json({ message: 'Internal server error' });
    }
});

app.get('/api/get-data', async (req, res) => {
    try {
        const data = await getProductData();
        res.json(data);
    } catch (err) {
        console.error('Error fetching product data:', err);
        res.status(500).json({ message: 'Internal server error' });
    }
});

app.post('/api/update-config', (req, res) => {
    const { imageDirectory } = req.body;

    if (!imageDirectory) {
        return res.status(400).json({ message: 'Invalid configuration' });
    }

    // Update the configuration file
    fs.writeFileSync(path.join(__dirname, 'config.json'), JSON.stringify({ imageDirectory }, null, 2));

    // Update the static file serving middleware
    app.use('/images', express.static(imageDirectory));

    res.status(200).json({ message: 'Configuration updated successfully' });
});

app.get('/api/get-serial-numbers', async (req, res) => {
    const productId = req.query.id_product;
    if (!productId) {
        return res.status(400).json({ error: 'Product ID is required' });
    }

    try {
        const serialNumbers = await db.getSerialNumbers(productId);
        res.json(serialNumbers);
    } catch (error) {
        res.status(500).json({ error: 'Error fetching serial numbers' });
    }
});

const server = app.listen(port, () => {
    console.log(`Server running on port ${port}`);
});

// WebSocket server setup
const wss = new WebSocket.Server({ server });

wss.on('connection', (ws) => {
    console.log('Client connected');

    ws.on('close', () => {
        console.log('Client disconnected');
    });
});

// Function to broadcast data to all connected clients
const broadcastData = async () => {
    try {
        const data = await getProductData();
        wss.clients.forEach((client) => {
            if (client.readyState === WebSocket.OPEN) {
                client.send(JSON.stringify(data));
            }
        });
    } catch (err) {
        console.error('Error broadcasting data:', err);
    }
};

// Example: Periodically broadcast data every 10 seconds
setInterval(broadcastData, 10000);