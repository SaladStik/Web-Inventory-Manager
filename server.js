const express = require('express');
const bodyParser = require('body-parser');
const path = require('path');
const { validateUser } = require('./database'); // Assuming you have a database.js file with validateUser function

const app = express();
const port = 3000;

app.use(bodyParser.json());
app.use(express.static(path.join(__dirname, 'public'))); // Serve static files from the 'public' directory

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

app.listen(port, () => {
    console.log(`Server running on port ${port}`);
});