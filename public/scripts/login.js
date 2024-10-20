document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('login-form');
    console.log('DOM fully loaded and parsed');

    form.addEventListener('submit', async (event) => {
        event.preventDefault(); // Prevent the default form submission
        console.log('Form submission intercepted');

        const formData = new FormData(form);
        const data = Object.fromEntries(formData.entries());

        console.log('Form data:', data); // Log form data to verify

        try {
            const response = await fetch('/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(data)
            });

            console.log('Response status:', response.status); // Log response status

            if (response.ok) {
                // Handle successful response
                const result = await response.json();
                console.log(result);
                window.location.href = result.redirectUrl; // Redirect to the main menu
            } else {
                // Handle errors
                const errorResult = await response.json();
                console.error('Error:', errorResult.message);
                alert(errorResult.message); // Show error message to the user
            }
        } catch (error) {
            console.error('Error:', error);
            alert('An error occurred. Please try again later.');
        }
    });
});